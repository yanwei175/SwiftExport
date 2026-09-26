namespace SwiftExportClient.Services.Images;

/// <summary>本地与网络图片缩略图缓存配置；网络图片只进入内存缓存。</summary>
public sealed class ImageCacheOptions
{
    public const string SectionName = "ImageCache";

    public int MemoryLimitMegabytes { get; set; } = 64;

    public int MemoryEntryLimit { get; set; } = 512;

    public int DiskLimitMegabytes { get; set; } = 512;

    public double DiskCleanupTargetRatio { get; set; } = 0.8;

    public int ExpirationDays { get; set; } = 30;

    public int DefaultDecodePixelWidth { get; set; } = 96;

    public int MaximumDecodePixelWidth { get; set; } = 2048;

    public int MaximumSourceFileMegabytes { get; set; } = 50;

    public int MaximumConcurrentDecodes { get; set; } = 2;

    public int NegativeCacheSeconds { get; set; } = 30;

    /// <summary>网络响应头及响应体读取的超时秒数。</summary>
    public int HttpTimeoutSeconds { get; set; } = 15;

    /// <summary>网络图片在内存缓存中的有效秒数。</summary>
    public int HttpMemoryCacheSeconds { get; set; } = 60;

    /// <summary>为空时使用 %LOCALAPPDATA%\SwiftExport\Cache\Images\v1。</summary>
    public string? DiskCachePath { get; set; }
}
