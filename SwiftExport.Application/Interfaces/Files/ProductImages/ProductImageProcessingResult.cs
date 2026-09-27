namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>承载已完成方向、白底、JPEG 编码及缩略图来源标记的单图结果。</summary>
public sealed class ProductImageProcessingResult
{
    /// <summary>获取最终原图 JPEG 字节。</summary>
    public required byte[] OriginalBytes { get; init; }

    /// <summary>获取已内嵌最终原图来源指纹的缩略图 JPEG 完整字节。</summary>
    public required byte[] ThumbnailBytes { get; init; }

    /// <summary>获取方向修正后的原图宽度。</summary>
    public required int OriginalWidth { get; init; }

    /// <summary>获取方向修正后的原图高度。</summary>
    public required int OriginalHeight { get; init; }

    /// <summary>获取缩略图宽度。</summary>
    public required int ThumbnailWidth { get; init; }

    /// <summary>获取缩略图高度。</summary>
    public required int ThumbnailHeight { get; init; }
}
