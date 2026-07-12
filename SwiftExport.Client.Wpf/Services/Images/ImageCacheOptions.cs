namespace SwiftExportClient.Services.Images;

/// <summary>本地图片缩略图缓存配置。</summary>
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

    /// <summary>为空时使用 %LOCALAPPDATA%\SwiftExport\Cache\Images\v1。</summary>
    public string? DiskCachePath { get; set; }
}
