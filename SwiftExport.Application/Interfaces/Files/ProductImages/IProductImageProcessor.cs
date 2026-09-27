namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>定义与具体图像库隔离的产品图片解码和双 JPEG 编码边界。</summary>
public interface IProductImageProcessor
{
    /// <summary>验证实际编码、帧数与像素上限，并生成原图及带来源标签缩略图的最终字节。</summary>
    Task<ProductImageProcessingResult> ProcessAsync(
        Stream source,
        ProductImageInputFormat expectedFormat,
        long maxFileBytes,
        long maxPixels,
        CancellationToken cancellationToken = default);
}
