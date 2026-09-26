using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Options;

namespace SwiftExportClient.Services.Images;

/// <summary>为本地文件提供内存/磁盘缩略图缓存，并为完整 HTTP/HTTPS 地址提供限时内存缓存。</summary>
public sealed class ImageCacheService : IImageCacheService, IDisposable
{
    /// <summary>获取不携带业务认证信息的网络图片客户端注册名称。</summary>
    public const string HttpClientName = "SwiftExport.ImageCache";

    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        UseCookies = false,
        UseDefaultCredentials = false,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly object _memoryGate = new();
    private readonly Dictionary<string, MemoryEntry> _memory = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<MemoryEntry?>>> _inflight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _negativeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _decodeSemaphore;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ImageCacheOptions _options;
    private readonly string _diskCachePath;
    private readonly long _memoryLimitBytes;
    private readonly long _diskLimitBytes;
    private long _memoryBytes;
    private long _memoryHits;
    private long _diskHits;
    private long _decodeCount;
    private bool _disposed;

    /// <summary>从依赖注入获取配置及独立网络图片客户端。</summary>
    public ImageCacheService(IOptions<ImageCacheOptions> options, IHttpClientFactory httpClientFactory)
        : this(
            options.Value,
            httpClientFactory.CreateClient(HttpClientName),
            TimeProvider.System,
            disposeHttpClient: true)
    {
    }

    /// <summary>供测试或独立宿主显式传入配置。</summary>
    public ImageCacheService(ImageCacheOptions options)
        : this(options, SharedHttpClient, TimeProvider.System, disposeHttpClient: false)
    {
    }

    /// <summary>供测试或独立宿主注入受控 HTTP 响应与时间源。</summary>
    public ImageCacheService(ImageCacheOptions options, HttpClient httpClient, TimeProvider? timeProvider = null)
        : this(options, httpClient, timeProvider, disposeHttpClient: false)
    {
    }

    /// <summary>初始化缓存并明确网络客户端的资源所有权。</summary>
    private ImageCacheService(
        ImageCacheOptions options,
        HttpClient httpClient,
        TimeProvider? timeProvider,
        bool disposeHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ValidateOptions(options);
        _options = options;
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private ImageRequest? CreateRequest(string sourcePath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var width = decodePixelWidth <= 0 ? _options.DefaultDecodePixelWidth : decodePixelWidth;
        if (width > _options.MaximumDecodePixelWidth)
        {
            return null;
        }

        var trimmedSource = sourcePath.Trim();
        if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            // 网络键保留调用方给出的完整地址大小写，避免大小写敏感路径或查询串发生串图。
            var key = HashKey($"http|{trimmedSource}|{width}");
            if (IsNegativeCached(key))
            {
                return null;
            }

            return new ImageRequest(trimmedSource, uri, width, key, key);
        }

        if (!Path.IsPathFullyQualified(sourcePath))
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

        if (Directory.Exists(fullPath))
        {
            return null;
        }

        var negativeKey = $"{fullPath}|{width}";
        if (IsNegativeCached(negativeKey))
        {
            return null;
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
            var key = HashKey(keyText);
            return new ImageRequest(fullPath, null, width, key, negativeKey);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            RememberFailure(negativeKey);
            return null;
        }
    }

    private async Task<MemoryEntry?> LoadOrCreateAsync(ImageRequest request)
    {
        if (!request.IsNetwork && TryLoadDisk(request.Key, out var diskEntry))
        {
            Interlocked.Increment(ref _diskHits);
            AddMemory(request.Key, diskEntry);
            return diskEntry;
        }

        await _decodeSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            MemoryEntry? entry;
            try
            {
                entry = request.IsNetwork
                    ? await LoadNetworkAsync(request).ConfigureAwait(false)
                    : await Task.Run(() => DecodeLocal(request.Source, request.DecodePixelWidth)).ConfigureAwait(false);
                if (entry is null)
                {
                    RememberFailure(request.NegativeKey);
                    return null;
                }

                Interlocked.Increment(ref _decodeCount);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               NotSupportedException or FileFormatException or HttpRequestException or
                                               OperationCanceledException)
            {
                RememberFailure(request.NegativeKey);
                return null;
            }

            AddMemory(request.Key, entry);
            if (!request.IsNetwork)
            {
                await WriteDiskAsync(request.Key, entry.Bitmap).ConfigureAwait(false);
                await CleanupDiskAsync().ConfigureAwait(false);
            }

            return entry;
        }
        finally
        {
            _decodeSemaphore.Release();
        }
    }

    /// <summary>下载网络响应并在内存中限量解码；不创建临时文件或磁盘缩略图。</summary>
    private async Task<MemoryEntry?> LoadNetworkAsync(ImageRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Get, request.NetworkUri);
        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var maximumBytes = _options.MaximumSourceFileMegabytes * 1024L * 1024L;
        if (response.Content.Headers.ContentLength is long contentLength &&
            (contentLength <= 0 || contentLength > maximumBytes))
        {
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffered = new MemoryStream(
            response.Content.Headers.ContentLength is long knownLength
                ? (int)Math.Min(knownLength, 1024 * 1024)
                : 64 * 1024);
        if (!await CopyWithLimitAsync(source, buffered, maximumBytes, timeout.Token).ConfigureAwait(false))
        {
            return null;
        }

        buffered.Position = 0;
        var expiresAt = _timeProvider.GetUtcNow().AddSeconds(_options.HttpMemoryCacheSeconds);
        return await Task.Run(
            () => Decode(buffered, request.DecodePixelWidth, expiresAt),
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>流式复制网络响应，并在写入缓存前拒绝超过源文件上限的响应体。</summary>
    private static async Task<bool> CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var totalBytes = 0L;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return totalBytes > 0;
                }

                if (totalBytes > maximumBytes - read)
                {
                    return false;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static MemoryEntry DecodeLocal(string sourcePath, int decodePixelWidth)
    {
        using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        return Decode(stream, decodePixelWidth, expiresAt: null);
    }

    private static MemoryEntry Decode(Stream stream, int decodePixelWidth, DateTimeOffset? expiresAt)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new MemoryEntry(bitmap, EstimateBytes(bitmap), expiresAt);
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
            entry = new MemoryEntry(bitmap, EstimateBytes(bitmap), expiresAt: null);
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

            if (entry.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= _timeProvider.GetUtcNow())
            {
                _memory.Remove(key);
                _lru.Remove(entry.Node);
                _memoryBytes -= entry.EstimatedBytes;
                entry = default!;
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
        _negativeCache[key] = _timeProvider.GetUtcNow().AddSeconds(_options.NegativeCacheSeconds);

    private bool IsNegativeCached(string key)
    {
        if (!_negativeCache.TryGetValue(key, out var retryAfter))
        {
            return false;
        }

        if (retryAfter > _timeProvider.GetUtcNow())
        {
            return true;
        }

        _negativeCache.TryRemove(key, out _);
        return false;
    }

    private static string HashKey(string keyText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyText)));

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
            options.NegativeCacheSeconds <= 0 || options.HttpTimeoutSeconds <= 0 ||
            options.HttpMemoryCacheSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ImageCache 配置值无效。");
        }
    }

    /// <summary>表示一次已规范化的本地或网络图片请求。</summary>
    private sealed record ImageRequest(
        string Source,
        Uri? NetworkUri,
        int DecodePixelWidth,
        string Key,
        string NegativeKey)
    {
        public bool IsNetwork => NetworkUri is not null;
    }

    /// <summary>保存冻结位图、容量估算及可选的网络新鲜期。</summary>
    private sealed class MemoryEntry(BitmapSource bitmap, long estimatedBytes, DateTimeOffset? expiresAt)
    {
        public BitmapSource Bitmap { get; } = bitmap;

        public long EstimatedBytes { get; } = estimatedBytes;

        public DateTimeOffset? ExpiresAt { get; } = expiresAt;

        public LinkedListNode<string> Node { get; set; } = null!;
    }
}
