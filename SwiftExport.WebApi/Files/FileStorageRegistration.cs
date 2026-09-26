using DvFileStorage.Abstractions;
using DvFileStorage.Configuration;
using DvFileStorage.Local;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.ProductImages;
using SwiftExport.WebApi.Files.ProductImages;

namespace SwiftExport.WebApi.Files;

/// <summary>把宿主 FileStorage 根目录配置转换为独立文件存储实例，不改变图片 HTTP 开关语义。</summary>
public static class FileStorageRegistration
{
    /// <summary>
    /// 当 RootPath 显式配置时校验并注册线程安全的本地存储；缺失或空值时保持未注册状态。
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

        var reservedRoots = new[] { productImages.ImageDirectoryPath, productImages.StateDirectoryPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        var containers = ConfiguredFileContainerFactory.Create(configuration, reservedRoots);
        services.AddSingleton<IFileContainerProvider>(containers);
        return services;
    }
}
