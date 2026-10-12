using System.IO;
using System.Security.Cryptography;
using System.Text;
using SwiftExportClient.Services.Files.Internal;

namespace SwiftExportClient.Services.Files.ServerTemplates;

/// <summary>维护版本内容缓存；每次读取验证长度、散列和 XLSX，独占临时文件发布后按访问时间回收配额。</summary>
internal sealed class ServerTemplateCache
{
    private readonly ServerTemplateCacheOptions _options;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>建立服务专属缓存目录，所有文件名仅由隔离键散列生成。</summary>
    internal ServerTemplateCache(ServerTemplateCacheOptions options)
    {
        _options = options;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCacheBytes);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        options.ReadLimits.Validate();
        if (options.DownloadTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
        _root = string.IsNullOrWhiteSpace(options.CacheRootPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwiftExport", "Cache", "ExcelTemplates", "v1")
            : Path.GetFullPath(options.CacheRootPath);
    }

    /// <summary>将 API 环境、用户、模板及内容版本组合为不可反推业务路径的缓存键。</summary>
    internal static string Key(string server, string userId, ServerExcelTemplateInfo template) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{server}\n{userId}\n{template.TemplateId}\n{template.TemplateVersion}\n{template.Sha256}")));

    /// <summary>只返回完整有效的内存副本；被截断、篡改或格式异常的缓存删除后重新在线获取。</summary>
    internal async Task<byte[]?> ReadAsync(string key, ServerExcelTemplateInfo template, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_root, key + ".xlsx");
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes;
                await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var buffer = await FileInput.ReadBoundedAsync(source, _options.ReadLimits, token).ConfigureAwait(false))
                    bytes = buffer.ToArray();
                // 先关闭文件再校验及更新访问时间，避免 Windows 打开的只读句柄阻止属性修改或损坏项删除。
                using var input = new MemoryStream(bytes, writable: false);
                ValidateContent(input, bytes, template, _options.ReadLimits, token);
                if (bytes.LongLength > _options.MaxCacheBytes) File.Delete(path);
                else
                {
                    File.SetLastWriteTimeUtc(path, _options.TimeProvider.GetUtcNow().UtcDateTime);
                    EvictBeforePublish(bytes.LongLength, path, token);
                }
                return bytes;
            }
            catch (Exception exception) when (exception is FileOperationException or IOException or UnauthorizedAccessException)
            {
                TryDelete(path);
                return null;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>核对清单所承诺的完整文件与无宏 XLSX 容器，不相信 Content-Length 或扩展名。</summary>
    internal static void ValidateContent(MemoryStream input, byte[] bytes, ServerExcelTemplateInfo template,
        FileReadLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (bytes.LongLength != template.FileLength)
            throw new FileOperationException("TemplateLengthMismatch", "服务器模板内容长度与目录版本不一致。");
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), template.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new FileOperationException("TemplateHashMismatch", "服务器模板内容校验失败。");
        FileInput.ValidateXlsx(input, limits, token);
    }

    /// <summary>写入同目录临时文件；最终移动由会话提交保护执行，旧会话不得发布缓存。</summary>
    internal async Task StoreAsync(string key, byte[] bytes, Action<Action> commit, CancellationToken token)
    {
        if (bytes.LongLength > _options.MaxCacheBytes) return;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(_root);
            temporary = Path.Combine(_root, $".download-{Guid.NewGuid():N}.tmp");
            var target = Path.Combine(_root, key + ".xlsx");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            // 访问时间属于提交前准备；移动完成后不再因属性写入失败误报已经发布的缓存。
            File.SetLastWriteTimeUtc(temporary, _options.TimeProvider.GetUtcNow().UtcDateTime);
            EvictBeforePublish(bytes.LongLength, target, token);
            commit(() =>
            {
                token.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite: true);
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new FileOperationException("TemplateCacheWriteFailed", "无法发布本地模板缓存。", exception);
        }
        finally
        {
            if (temporary is not null) TryDelete(temporary);
            _gate.Release();
        }
    }

    /// <summary>只管理服务自己生成的散列 XLSX 文件；发布前回收最久未访问项，不扫描其他目录。</summary>
    private void EvictBeforePublish(long incomingBytes, string target, CancellationToken token)
    {
        var files = new DirectoryInfo(_root).EnumerateFiles("*.xlsx", SearchOption.TopDirectoryOnly)
            .Where(file => file.Name.Length == 69 && file.Name[..64].All(Uri.IsHexDigit))
            .Where(file => !string.Equals(file.FullName, target, StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToArray();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (total <= _options.MaxCacheBytes - incomingBytes) break;
            var length = file.Length;
            file.Delete();
            total -= length;
        }
    }

    /// <summary>缓存清理失败不遮蔽原来的内容或发布异常。</summary>
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
