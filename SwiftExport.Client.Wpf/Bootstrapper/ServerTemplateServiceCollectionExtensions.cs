using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwiftExport.Contracts.Apis.Files;
using SwiftExportClient.Services.Files.ServerTemplates;

namespace SwiftExportClient.Bootstrapper;

/// <summary>在完整认证和 API 宿主中注册服务器模板入口，保持基础文件生成器可独立使用。</summary>
public static class ServerTemplateServiceCollectionExtensions
{
    /// <summary>注册持有账号事件和缓存协调状态的单例；HTTP 与认证依赖由客户端组合根提供。</summary>
    public static IServiceCollection AddServerExcelTemplateService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // 每次列表查询创建当前配置的 Refit 客户端，避免单例服务持有切换服务器前的基地址。
        services.TryAddSingleton<Func<IExportTemplateApi>>(provider =>
            () => provider.GetRequiredService<IExportTemplateApi>());
        services.TryAddSingleton<IServerExcelTemplateService, ServerExcelTemplateService>();
        return services;
    }
}
