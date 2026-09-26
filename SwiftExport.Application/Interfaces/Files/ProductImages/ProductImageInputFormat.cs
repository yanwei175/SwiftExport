namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>表示由受控文件扩展名声明、仍需用实际内容复核的输入图片编码。</summary>
public enum ProductImageInputFormat
{
    /// <summary>JPEG 编码。</summary>
    Jpeg,

    /// <summary>PNG 编码。</summary>
    Png,

    /// <summary>WebP 编码。</summary>
    Webp
}
