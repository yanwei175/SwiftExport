namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>保存公开产品图片入口与批量上传共享的启动配置快照。</summary>
public sealed class ProductImageStorageOptions
{
    /// <summary>获取既有公开静态图片入口是否启用。</summary>
    public bool PublicHostingEnabled { get; init; }

    /// <summary>获取批量上传入口是否显式启用。</summary>
    public bool UploadEnabled { get; init; }

    /// <summary>获取已验证的 FileStorage 绝对根目录；两个入口均关闭时可为空。</summary>
    public string? RootPath { get; init; }

    /// <summary>获取根目录内产品图片的单目录名。</summary>
    public string DirectoryName { get; init; } = "ProductImages";

    /// <summary>获取公开图片固定请求路径。</summary>
    public string RequestPath { get; init; } = "/files/ProductImages";

    /// <summary>获取上传返回地址使用的部署基址。</summary>
    public Uri? ApiBaseUri { get; init; }

    /// <summary>获取单文件压缩内容上限。</summary>
    public long MaxFileBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>获取包含 multipart 开销的批次请求上限。</summary>
    public long MaxBatchBytes { get; init; } = 25 * 1024 * 1024;

    /// <summary>获取单批最大文件数。</summary>
    public int MaxFiles { get; init; } = 20;

    /// <summary>获取单图最大解码像素数。</summary>
    public long MaxPixels { get; init; } = 24_000_000;

    /// <summary>获取当前进程允许的图像原生解码/编码并发数。</summary>
    public int MaxConcurrentProcessing { get; init; } = 2;

    internal string? ImageDirectoryPath => RootPath is null ? null : Path.Combine(RootPath, DirectoryName);
    internal string? StateDirectoryPath => RootPath is null ? null : Path.Combine(RootPath, ".product-image-state");
}
