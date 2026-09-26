using DvFileStorage.Abstractions;
using DvFileStorage.Configuration;
using DvFileStorage.Exceptions;
using DvFileStorage.Models;

namespace DvFileStorage.Local;

/// <summary>
/// 在一个固定本地根目录内提供流式文件操作，并通过同卷临时文件保证只发布完整内容。
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    /// <summary>清理内部临时文件失败时，原始异常 <see cref="Exception.Data"/> 中使用的稳定键。</summary>
    public const string CleanupFailureDataKey = "DvFileStorage.TempCleanupFailure";

    private const string TemporaryDirectoryName = ".dvfilestorage-tmp";
    private const int CopyBufferSize = 81920;
    private static readonly char[] WindowsInvalidFileNameCharacters = ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly HashSet<string> WindowsReservedNames = BuildWindowsReservedNames();
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly string _temporaryDirectoryPath;

    /// <summary>使用配置快照创建本地存储；根目录必须已存在、可访问且不能是重解析点。</summary>
    public LocalFileStorage(LocalFileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathFullyQualified(options.RootPath))
        {
            throw new ArgumentException("RootPath 必须是已存在的绝对目录。", nameof(options));
        }

        string fullRoot;
        try
        {
            fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("RootPath 不是有效的绝对目录。", nameof(options), exception);
        }

        if (!TryGetAttributes(fullRoot, out var attributes))
        {
            throw new DirectoryNotFoundException("配置的本地文件存储根目录不存在。");
        }

        if (!attributes.HasFlag(FileAttributes.Directory))
        {
            throw new ArgumentException("RootPath 必须指向目录。", nameof(options));
        }

        RejectReparsePoint(fullRoot, attributes);
        _rootPath = fullRoot;
        _rootPrefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        _temporaryDirectoryPath = Path.Combine(fullRoot, TemporaryDirectoryName);
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        string path,
        Stream source,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("源流必须可读。", nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path);
        EnsureParentDirectories(resolved);
        if (TryGetAttributes(resolved.PhysicalPath, out var targetAttributes))
        {
            ValidateFileTarget(resolved.LogicalPath, resolved.PhysicalPath, targetAttributes);
            if (!overwrite)
            {
                throw new FileAlreadyExistsException(resolved.LogicalPath);
            }
        }

        EnsureTemporaryDirectory();
        var temporaryPath = Path.Combine(_temporaryDirectoryPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // 这是取消提交点。发布成功后不再检查令牌，避免把已提交写入报告为取消。
            cancellationToken.ThrowIfCancellationRequested();
            PublishTemporaryFile(temporaryPath, resolved, overwrite);
            temporaryPath = string.Empty;
        }
        catch (Exception exception)
        {
            if (temporaryPath.Length > 0)
            {
                TryCleanupTemporaryFile(temporaryPath, exception);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path);
        var state = InspectExistingPath(resolved, out _);
        if (state == PathState.Missing)
        {
            throw new FileNotFoundException("存储文件不存在。", resolved.LogicalPath);
        }

        try
        {
            Stream stream = new FileStream(
                resolved.PhysicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException("存储文件不存在。", resolved.LogicalPath, exception);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path);
        var state = InspectExistingPath(resolved, out _);
        return Task.FromResult(state == PathState.File);
    }

    /// <inheritdoc />
    public Task<StoredFileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path);
        var state = InspectExistingPath(resolved, out _);
        if (state == PathState.Missing)
        {
            return Task.FromResult<StoredFileInfo?>(null);
        }

        try
        {
            var file = new FileInfo(resolved.PhysicalPath);
            var result = new StoredFileInfo
            {
                Path = resolved.LogicalPath,
                Length = file.Length,
                LastModifiedUtc = new DateTimeOffset(file.LastWriteTimeUtc)
            };
            return Task.FromResult<StoredFileInfo?>(result);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<StoredFileInfo?>(null);
        }
    }

    /// <inheritdoc />
    public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Resolve(path);
        var state = InspectExistingPath(resolved, out _);
        if (state == PathState.Missing)
        {
            return Task.CompletedTask;
        }

        // 删除是本方法的提交点；成功后不再检查取消，避免把已删除结果报告为取消。
        File.Delete(resolved.PhysicalPath);
        return Task.CompletedTask;
    }

    /// <summary>校验逻辑路径并生成受根目录边界约束的物理路径。</summary>
    private ResolvedPath Resolve(string path)
    {
        EnsureRootAvailable();
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("文件路径不能为空。", nameof(path));
        }

        if (path.Contains('\\') || Path.IsPathFullyQualified(path) || path.StartsWith('/'))
        {
            throw new ArgumentException("文件路径必须是使用正斜线分隔的逻辑相对路径。", nameof(path));
        }

        var segments = path.Split('/', StringSplitOptions.None);
        foreach (var segment in segments)
        {
            ValidateSegment(segment, path);
        }

        if (segments[0].Equals(TemporaryDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("文件路径不能访问存储内部临时目录。", nameof(path));
        }

        var combinedPath = _rootPath;
        foreach (var segment in segments)
        {
            combinedPath = Path.Combine(combinedPath, segment);
        }

        var physicalPath = Path.GetFullPath(combinedPath);
        if (!physicalPath.StartsWith(_rootPrefix, PathComparison))
        {
            throw new ArgumentException("文件路径越出了存储根目录。", nameof(path));
        }

        return new ResolvedPath(string.Join('/', segments), segments, physicalPath);
    }

    /// <summary>拒绝空段、穿越、ADS、Windows 不稳定名称及保留设备名。</summary>
    private static void ValidateSegment(string segment, string path)
    {
        if (segment.Length == 0 || segment is "." or ".." ||
            segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.IndexOfAny(WindowsInvalidFileNameCharacters) >= 0 ||
            segment.Any(character => character == '\0' || char.IsControl(character)))
        {
            throw new ArgumentException($"文件路径包含不允许的名称段：{path}", nameof(path));
        }

        var deviceCandidate = segment.Split('.', 2)[0];
        if (WindowsReservedNames.Contains(deviceCandidate))
        {
            throw new ArgumentException($"文件路径包含 Windows 保留设备名：{path}", nameof(path));
        }
    }

    /// <summary>创建合法父目录，并在每个已存在或新建段上拒绝重解析点。</summary>
    private void EnsureParentDirectories(ResolvedPath resolved)
    {
        var current = _rootPath;
        for (var index = 0; index < resolved.Segments.Length - 1; index++)
        {
            current = Path.Combine(current, resolved.Segments[index]);
            if (!TryGetAttributes(current, out var attributes))
            {
                Directory.CreateDirectory(current);
                attributes = File.GetAttributes(current);
            }

            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                throw new IOException($"父路径不是目录：{resolved.LogicalPath}");
            }

            RejectReparsePoint(current, attributes);
        }
    }

    /// <summary>检查已有路径段，缺失返回 Missing，目录目标和重解析点作为明确故障抛出。</summary>
    private PathState InspectExistingPath(ResolvedPath resolved, out FileAttributes targetAttributes)
    {
        var current = _rootPath;
        for (var index = 0; index < resolved.Segments.Length; index++)
        {
            current = Path.Combine(current, resolved.Segments[index]);
            if (!TryGetAttributes(current, out var attributes))
            {
                targetAttributes = default;
                return PathState.Missing;
            }

            RejectReparsePoint(current, attributes);
            var isTarget = index == resolved.Segments.Length - 1;
            if (!isTarget && !attributes.HasFlag(FileAttributes.Directory))
            {
                throw new IOException($"父路径不是目录：{resolved.LogicalPath}");
            }

            if (isTarget)
            {
                ValidateFileTarget(resolved.LogicalPath, current, attributes);
                targetAttributes = attributes;
                return PathState.File;
            }
        }

        targetAttributes = default;
        return PathState.Missing;
    }

    /// <summary>确保目标是普通文件而不是目录或重解析入口。</summary>
    private static void ValidateFileTarget(string logicalPath, string physicalPath, FileAttributes attributes)
    {
        RejectReparsePoint(physicalPath, attributes);
        if (attributes.HasFlag(FileAttributes.Directory))
        {
            throw new IOException($"目标路径不是文件：{logicalPath}");
        }
    }

    /// <summary>按需创建模块专用同卷临时目录，并拒绝将重解析点用作发布来源。</summary>
    private void EnsureTemporaryDirectory()
    {
        if (!TryGetAttributes(_temporaryDirectoryPath, out var attributes))
        {
            Directory.CreateDirectory(_temporaryDirectoryPath);
            attributes = File.GetAttributes(_temporaryDirectoryPath);
        }

        if (!attributes.HasFlag(FileAttributes.Directory))
        {
            throw new IOException("内部临时路径不是目录。");
        }

        RejectReparsePoint(_temporaryDirectoryPath, attributes);
    }

    /// <summary>每次操作确认根目录仍是普通目录；无法消除本机攻击者在检查后替换目录的竞争窗口。</summary>
    private void EnsureRootAvailable()
    {
        if (!TryGetAttributes(_rootPath, out var attributes) || !attributes.HasFlag(FileAttributes.Directory))
        {
            throw new DirectoryNotFoundException("本地文件存储根目录不存在。");
        }

        RejectReparsePoint(_rootPath, attributes);
    }

    /// <summary>尝试读取路径属性，仅把标准的路径缺失异常解释为不存在。</summary>
    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    /// <summary>首期拒绝符号链接、junction 等重解析点，避免跟随到配置根目录之外。</summary>
    private static void RejectReparsePoint(string path, FileAttributes attributes)
    {
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"本地文件存储不允许访问重解析点：{Path.GetFileName(path)}");
        }
    }

    /// <summary>清理本模块临时文件；失败时保留原异常类型并附加清理异常。</summary>
    private static void TryCleanupTemporaryFile(string temporaryPath, Exception originalException)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception cleanupException)
        {
            originalException.Data[CleanupFailureDataKey] = cleanupException;
        }
    }

    /// <summary>
    /// 通过同卷移动或替换发布完整文件；并发目标变化时重新判定，绝不先删除正式文件。
    /// </summary>
    private static void PublishTemporaryFile(string temporaryPath, ResolvedPath resolved, bool overwrite)
    {
        if (!overwrite)
        {
            try
            {
                File.Move(temporaryPath, resolved.PhysicalPath, overwrite: false);
            }
            catch (IOException exception)
            {
                if (!TryGetAttributes(resolved.PhysicalPath, out var attributes)) throw;
                ValidateFileTarget(resolved.LogicalPath, resolved.PhysicalPath, attributes);
                throw new FileAlreadyExistsException(resolved.LogicalPath, exception);
            }
            return;
        }

        IOException? lastConcurrentFailure = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (!TryGetAttributes(resolved.PhysicalPath, out var attributes))
            {
                try
                {
                    File.Move(temporaryPath, resolved.PhysicalPath, overwrite: false);
                    return;
                }
                catch (IOException) when (TryGetAttributes(resolved.PhysicalPath, out _))
                {
                    // 另一个写入者刚刚发布目标；下一轮按覆盖语义替换它。
                    Thread.Yield();
                }
            }
            else
            {
                ValidateFileTarget(resolved.LogicalPath, resolved.PhysicalPath, attributes);
                try
                {
                    File.Replace(temporaryPath, resolved.PhysicalPath, destinationBackupFileName: null);
                    return;
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    // 目标在检查后被删除；下一轮重新选择移动或替换。
                }
                catch (IOException exception)
                {
                    // Windows 并发替换可能短暂争用目标目录项；目标仍存在时有限重试。
                    lastConcurrentFailure = exception;
                    Thread.Yield();
                }
            }
        }

        throw new IOException($"目标文件在发布期间持续变化：{resolved.LogicalPath}", lastConcurrentFailure);
    }

    private static HashSet<string> BuildWindowsReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$"
        };
        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }

        foreach (var suffix in new[] { '¹', '²', '³' })
        {
            names.Add($"COM{suffix}");
            names.Add($"LPT{suffix}");
        }

        return names;
    }

    private sealed record ResolvedPath(string LogicalPath, string[] Segments, string PhysicalPath);

    private enum PathState
    {
        Missing,
        File
    }
}
