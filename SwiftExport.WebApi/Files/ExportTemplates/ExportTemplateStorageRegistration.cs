using SwiftExport.Application.Interfaces.Files;

namespace SwiftExport.WebApi.Files.ExportTemplates;

/// <summary>注册模板分发专用存储，不改变既有图片或通用文件容器的注册行为。</summary>
public static class ExportTemplateStorageRegistration
{
    /// <summary>校验启动配置并注册独立模板存储提供器；Application 清单服务由其组合根注册。</summary>
    public static IServiceCollection AddConfiguredExportTemplates(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IExportTemplateStorageProvider>(new ConfiguredExportTemplateStorageProvider(configuration));
        return services;
    }
}
