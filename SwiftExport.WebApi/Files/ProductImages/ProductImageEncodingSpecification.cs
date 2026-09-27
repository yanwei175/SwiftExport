namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>集中定义产品原图、缩略图编码参数和缩略图来源标签中的稳定规格文本。</summary>
internal static class ProductImageEncodingSpecification
{
    /// <summary>获取服务端最终原图的 JPEG 质量。</summary>
    public const int OriginalJpegQuality = 90;

    /// <summary>获取服务端缩略图的 JPEG 质量。</summary>
    public const int ThumbnailJpegQuality = 85;

    /// <summary>获取缩略图允许的最长边像素数。</summary>
    public const int ThumbnailMaxEdge = 256;

    /// <summary>获取写入 COM 标签并用于严格匹配的固定生成规格。</summary>
    public static string ThumbnailSourceSpecification { get; } =
        $"jpeg-v1;maxEdge={ThumbnailMaxEdge};quality={ThumbnailJpegQuality};" +
        "noUpscale=1;whiteBackground=1;orientationNormalized=1";
}
