namespace SwiftExportClient.Services.Images;

/// <summary>图片缓存的轻量诊断信息。</summary>
public sealed record ImageCacheStatistics(
    int MemoryEntryCount,
    long MemoryBytes,
    int DiskEntryCount,
    long DiskBytes,
    long MemoryHits,
    long DiskHits,
    long DecodeCount);
