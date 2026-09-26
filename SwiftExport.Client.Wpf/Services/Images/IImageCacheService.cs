using System.Windows.Media.Imaging;

namespace SwiftExportClient.Services.Images;

/// <summary>为本机、UNC 路径或完整 HTTP/HTTPS 地址提供有界的冻结缩略图缓存。</summary>
public interface IImageCacheService
{
    ValueTask<BitmapSource?> GetAsync(
        string sourcePath,
        int decodePixelWidth,
        CancellationToken cancellationToken = default);

    ImageCacheStatistics GetStatistics();

    Task ClearAsync(CancellationToken cancellationToken = default);
}
