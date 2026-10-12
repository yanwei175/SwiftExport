using DvFileStorage.Configuration;
using DvFileStorage.Local;
using SwiftExport.Application.Interfaces.Files.ProductImages;

namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>把宿主验证完成的产品图片配置转换为 Application 可用的专用存储快照。</summary>
public sealed class ConfiguredProductImageStorageProvider : IProductImageStorageProvider
{
    private readonly ProductImageStorageContext? _context;

    /// <summary>上传启用时建立以顶层根目录为边界的 DvFileStorage；不主动创建业务目录。</summary>
    public ConfiguredProductImageStorageProvider(ProductImageStorageOptions options)
    {
        if (!options.UploadEnabled) return;
        _context = new ProductImageStorageContext
        {
            Storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = options.RootPath! }),
            ImageDirectoryName = options.DirectoryName,
            MaxFileBytes = options.MaxFileBytes,
            MaxBatchBytes = options.MaxBatchBytes,
            MaxFiles = options.MaxFiles,
            MaxPixels = options.MaxPixels
        };
    }

    /// <inheritdoc />
    public bool TryGetEnabledStorage(out ProductImageStorageContext? context)
    {
        context = _context;
        return context is not null;
    }
}
