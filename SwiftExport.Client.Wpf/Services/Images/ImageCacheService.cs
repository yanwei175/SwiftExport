using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Options;

namespace SwiftExportClient.Services.Images;

/// <summary>使用内存 LRU 和磁盘缩略图实现的本地图片缓存。</summary>
public sealed class ImageCacheService : IImageCacheService, IDisposable
{
    private readonly object _memoryGate = new();
    private readonly Dictionary<string, MemoryEntry> _memory = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<MemoryEntry?>>> _inflight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _negativeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _decodeSemaphore;
    private readonly ImageCacheOptions _options;
    private readonly string _diskCachePath;
    private readonly long _memoryLimitBytes;
    private readonly long _diskLimitBytes;
    private long _memoryBytes;
    private long _memoryHits;
    private long _diskHits;
    private long _decodeCount;
    private bool _disposed;

    public ImageCacheService(IOptions<ImageCacheOptions> options)
        : this(options.Value)
    {
    }

    /// <summary>供测试或独立宿主显式传入配置。</summary>
    public ImageCacheService(ImageCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        _options = options;
        _memoryLimitBytes = options.MemoryLimitMegabytes * 1024L * 1024L;
        _diskLimitBytes = options.DiskLimitMegabytes * 1024L * 1024L;
        _diskCachePath = string.IsNullOrWhiteSpace(options.DiskCachePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SwiftExport",
                "Cache",
                "Images",
                "v1")
            : Path.GetFullPath(options.DiskCachePath);
        _decodeSemaphore = new SemaphoreSlim(options.MaximumConcurrentDecodes);
    }

    public async ValueTask<BitmapSource?> GetAsync(
        string sourcePath,
        int decodePixelWidth,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var request = await Task.Run(
            () => CreateRequest(sourcePath, decodePixelWidth),
            cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return null;
        }

        if (TryGetMemory(request.Key, out var cached))
        {
            Interlocked.Increment(ref _memoryHits);
            return cached.Bitmap;
        }

        var lazy = _inflight.GetOrAdd(
            request.Key,
            _ => new Lazy<Task<MemoryEntry?>>(
                () => LoadOrCreateAsync(request),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var sharedTask = lazy.Value;
        _ = sharedTask.ContinueWith(
            completedTask =>
            {
                if (_inflight.TryGetValue(request.Key, out var current) && ReferenceEquals(current, lazy))
                {
                    _inflight.TryRemove(request.Key, out var removed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var entry = await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return entry?.Bitmap;
    }

    public ImageCacheStatistics GetStatistics()
    {
        int memoryCount;
        long memoryBytes;
        lock (_memoryGate)
        {
            memoryCount = _memory.Count;
            memoryBytes = _memoryBytes;
        }

        var diskFiles = Directory.Exists(_diskCachePath)
            ? new DirectoryInfo(_diskCachePath).EnumerateFiles("*.png", SearchOption.TopDirectoryOnly).ToArray()
            : [];
        return new ImageCacheStatistics(
            memoryCount,
            memoryBytes,
            diskFiles.Length,
            diskFiles.Sum(file => file.Length),
            Interlocked.Read(ref _memoryHits),
            Interlocked.Read(ref _diskHits),
            Interlocked.Read(ref _decodeCount));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_memoryGate)
        {
            _memory.Clear();
            _lru.Clear();
            _memoryBytes = 0;
        }

        _negativeCache.Clear();
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(_diskCachePath))
            {
                return;
            }

            foreach (var file in new DirectoryInfo(_diskCachePath).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDelete(file.FullName);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _decodeSemaphore.Dispose();
    }

    private ImageRequest? CreateRequest(string sourcePath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var width = decodePixelWidth <= 0 ? _options.DefaultDecodePixelWidth : decodePixelWidth;
        if (width > _options.MaximumDecodePixelWidth || Directory.Exists(fullPath))
        {
            return null;
        }

        var negativeKey = $"{fullPath}|{width}";
        if (_negativeCache.TryGetValue(negativeKey, out var retryAfter))
        {
            if (retryAfter > DateTimeOffset.UtcNow)
            {
                return null;
            }

            _negativeCache.TryRemove(negativeKey, out _);
        }

        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists || file.Length <= 0 || file.Length > _options.MaximumSourceFileMegabytes * 1024L * 1024L)
            {
                RememberFailure(negativeKey);
                return null;
            }

            var keyText = $"{fullPath.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{width}";
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyText)));
            return new ImageRequest(fullPath, width, key, negativeKey);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            RememberFailure(negativeKey);
            return null;
        }
    }

    private async Task<MemoryEntry?> LoadOrCreateAsync(ImageRequest request)
    {
        if (TryLoadDisk(request.Key, out var diskEntry))
        {
            Interlocked.Increment(ref _diskHits);
            AddMemory(request.Key, diskEntry);
            return diskEntry;
        }

        await _decodeSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            MemoryEntry entry;
            try
            {
                entry = await Task.Run(() => Decode(request.SourcePath, request.DecodePixelWidth)).ConfigureAwait(false);
                Interlocked.Increment(ref _decodeCount);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
            {
                RememberFailure(request.NegativeKey);
                return null;
            }

            AddMemory(request.Key, entry);
            await WriteDiskAsync(request.Key, entry.Bitmap).ConfigureAwait(false);
            await CleanupDiskAsync().ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _decodeSemaphore.Release();
        }
    }

    private static MemoryEntry Decode(string sourcePath, int decodePixelWidth)
    {
        using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new MemoryEntry(bitmap, EstimateBytes(bitmap));
    }

    private bool TryLoadDisk(string key, out MemoryEntry entry)
    {
        entry = default!;
        var path = GetDiskPath(key);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return false;
            }

            if (file.LastAccessTimeUtc < DateTime.UtcNow.AddDays(-_options.ExpirationDays))
            {
                TryDelete(path);
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            entry = new MemoryEntry(bitmap, EstimateBytes(bitmap));
            if (file.LastAccessTimeUtc < DateTime.UtcNow.AddHours(-1))
            {
                file.LastAccessTimeUtc = DateTime.UtcNow;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            TryDelete(path);
            return false;
        }
    }

    private async Task WriteDiskAsync(string key, BitmapSource bitmap)
    {
        Directory.CreateDirectory(_diskCachePath);
        var target = GetDiskPath(key);
        if (File.Exists(target))
        {
            return;
        }

        var temporary = Path.Combine(_diskCachePath, $".{key}.{Guid.NewGuid():N}.tmp");
        try
        {
            await Task.Run(() =>
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                // 必须在发布临时文件前关闭流；using var 会延迟到整个 lambda 结束，
                // 导致 File.Move 移动一个仍被当前进程以 FileShare.None 打开的文件。
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(stream);
                    stream.Flush(flushToDisk: true);
                }

                try
                {
                    File.Move(temporary, target, overwrite: false);
                }
                catch (IOException) when (File.Exists(target))
                {
                    // 多个 ImageCacheService 实例可能同时发布同一 Key；先完成者即为有效缓存。
                    TryDelete(temporary);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporary);
        }
    }

    private Task CleanupDiskAsync() => Task.Run(() =>
    {
        if (!Directory.Exists(_diskCachePath))
        {
            return;
        }

        var expiration = DateTime.UtcNow.AddDays(-_options.ExpirationDays);
        var files = new DirectoryInfo(_diskCachePath).EnumerateFiles("*.png", SearchOption.TopDirectoryOnly).ToList();
        foreach (var expired in files.Where(file => file.LastAccessTimeUtc < expiration).ToArray())
        {
            TryDelete(expired.FullName);
            files.Remove(expired);
        }

        var total = files.Sum(file => file.Length);
        if (total <= _diskLimitBytes)
        {
            return;
        }

        var target = (long)(_diskLimitBytes * _options.DiskCleanupTargetRatio);
        foreach (var file in files.OrderBy(file => file.LastAccessTimeUtc))
        {
            if (total <= target)
            {
                break;
            }

            var length = file.Length;
            if (TryDelete(file.FullName))
            {
                total -= length;
            }
        }
    });

    private bool TryGetMemory(string key, out MemoryEntry entry)
    {
        lock (_memoryGate)
        {
            if (!_memory.TryGetValue(key, out entry!))
            {
                return false;
            }

            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
            return true;
        }
    }

    private void AddMemory(string key, MemoryEntry entry)
    {
        lock (_memoryGate)
        {
            if (_memory.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing.Node);
                _memoryBytes -= existing.EstimatedBytes;
            }

            entry.Node = _lru.AddFirst(key);
            _memory[key] = entry;
            _memoryBytes += entry.EstimatedBytes;
            while ((_memoryBytes > _memoryLimitBytes || _memory.Count > _options.MemoryEntryLimit) && _lru.Last is { } last)
            {
                var removeKey = last.Value;
                _lru.RemoveLast();
                if (_memory.Remove(removeKey, out var removed))
                {
                    _memoryBytes -= removed.EstimatedBytes;
                }
            }
        }
    }

    private string GetDiskPath(string key) => Path.Combine(_diskCachePath, $"{key}.png");

    private void RememberFailure(string key) =>
        _negativeCache[key] = DateTimeOffset.UtcNow.AddSeconds(_options.NegativeCacheSeconds);

    private static long EstimateBytes(BitmapSource bitmap) =>
        Math.Max(1L, bitmap.PixelWidth) * Math.Max(1, bitmap.PixelHeight) * Math.Max(32, bitmap.Format.BitsPerPixel) / 8;

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void ValidateOptions(ImageCacheOptions options)
    {
        if (options.MemoryLimitMegabytes <= 0 || options.MemoryEntryLimit <= 0 ||
            options.DiskLimitMegabytes <= 0 || options.DiskCleanupTargetRatio is <= 0 or >= 1 ||
            options.ExpirationDays <= 0 || options.DefaultDecodePixelWidth <= 0 ||
            options.MaximumDecodePixelWidth < options.DefaultDecodePixelWidth ||
            options.MaximumSourceFileMegabytes <= 0 || options.MaximumConcurrentDecodes <= 0 ||
            options.NegativeCacheSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ImageCache 配置值无效。");
        }
    }

    private sealed record ImageRequest(string SourcePath, int DecodePixelWidth, string Key, string NegativeKey);

    private sealed class MemoryEntry(BitmapSource bitmap, long estimatedBytes)
    {
        public BitmapSource Bitmap { get; } = bitmap;

        public long EstimatedBytes { get; } = estimatedBytes;

        public LinkedListNode<string> Node { get; set; } = null!;
    }
}
