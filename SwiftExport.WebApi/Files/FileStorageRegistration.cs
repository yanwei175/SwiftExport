using DvFileStorage.Abstractions;
using DvFileStorage.Configuration;
using DvFileStorage.Local;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.WebApi.Files.ProductImages;
using SwiftExport.WebApi.Files.ExportTemplates;

namespace SwiftExport.WebApi.Files;

/// <summary>把宿主 FileStorage 配置转换为图片、通用容器及模板专用存储；模板目录不暴露为可写容器。</summary>
public static class FileStorageRegistration
{
    /// <summary>
    /// 按各存储开关验证目录和隔离关系；基础 RootPath 缺失时仍允许启用独立模板目录。
    /// </summary>
    public static IServiceCollection AddConfiguredFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var productImages = ProductImageConfiguration.Create(configuration);
        services.AddSingleton(productImages);
        services.AddSingleton<IProductImageStorageProvider>(
            new ConfiguredProductImageStorageProvider(productImages));
        services.AddSingleton<IProductImageProcessor>(
            new SkiaProductImageProcessor(productImages.MaxConcurrentProcessing));

        var rootPath = configuration["FileStorage:RootPath"];
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            var storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = rootPath });
            var fullRootPath = Path.GetFullPath(rootPath);
            // 只读枚举用于启动时验证目录访问，不创建写权限探针或修改宿主目录。
            using (var entries = Directory.EnumerateFileSystemEntries(fullRootPath).GetEnumerator())
            {
                _ = entries.MoveNext();
            }

            services.AddSingleton<IFileStorage>(storage);
        }

        var legacyStateDirectoryPath = productImages.RootPath is null
            ? null
            : Path.Combine(
                productImages.RootPath,
                ProductImageConfiguration.LegacyProductImageStateDirectoryName);
        var reservedRoots = new[] { productImages.ImageDirectoryPath, legacyStateDirectoryPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        var containers = ConfiguredFileContainerFactory.Create(configuration, reservedRoots);
        services.AddSingleton<IFileContainerProvider>(containers);
        services.AddConfiguredExportTemplates(configuration);
        return services;
    }
}
