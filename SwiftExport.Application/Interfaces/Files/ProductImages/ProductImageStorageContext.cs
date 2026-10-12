using DvFileStorage.Abstractions;

namespace SwiftExport.Application.Interfaces.Files.ProductImages;

/// <summary>表示宿主验证完成的产品图片物理存储和资源限制快照；公开地址按批次读取系统参数。</summary>
public sealed class ProductImageStorageContext
{
    /// <summary>获取以 FileStorage 根目录为边界的底层存储。</summary>
    public required IFileStorage Storage { get; init; }

    /// <summary>获取根目录内的产品图片单目录名。</summary>
    public required string ImageDirectoryName { get; init; }

    /// <summary>获取单张上传源文件的最大字节数。</summary>
    public required long MaxFileBytes { get; init; }

    /// <summary>获取包含 multipart 开销的整批最大字节数。</summary>
    public required long MaxBatchBytes { get; init; }

    /// <summary>获取单批最大文件数。</summary>
    public required int MaxFiles { get; init; }

    /// <summary>获取单图解码允许的最大像素数。</summary>
    public required long MaxPixels { get; init; }
}
