using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files.ServerTemplates;
using SwiftExportClient.Services.Files.Templates;
using SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.Integration;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.PurchaseOrders;

/// <summary>通过正式 Refit、认证 HTTP 和 Report 注册生成采购示例，缓存与账号仅存在于隔离测试环境。</summary>
internal sealed class PurchaseOrderDemoClient : IDisposable
{
    private readonly ServiceProvider _provider;

    /// <summary>连接真实测试宿主，只替换测试账号与本地缓存路径，不依赖 VSTO 或业务数据服务。</summary>
    internal PurchaseOrderDemoClient(ServerTemplateApiHost host)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { UserId = host.UserId, UserName = "采购模板演示账号" });
        var tokens = new DemoTokenStore();
        var session = new SessionService(tokens, user);
        services.AddSingleton<ICurrentUserService>(user);
        services.AddSingleton<ITokenStore>(tokens);
        services.AddSingleton<ISessionService>(session);
        services.AddTransient<AuthTokenHandler>();
        services.Configure<ApiOptions>(options => options.BaseUrl = host.Address.AbsoluteUri);
        services.AddSwiftExportApis();
        services.AddClientFileServices();
        _provider = services.BuildServiceProvider();
        Service = new ServerExcelTemplateService(() => _provider.GetRequiredService<IExportTemplateApi>(),
            _provider.GetRequiredService<IHttpClientFactory>(), user, session,
            _provider.GetRequiredService<IExcelTemplateExportService>(),
            new ServerTemplateCacheOptions { CacheRootPath = Path.Combine(host.Root, "purchase-demo-cache") });
    }

    internal IServerExcelTemplateService Service { get; }

    /// <summary>先解除公共服务会话订阅，再释放测试客户端 HTTP 工厂。</summary>
    public void Dispose()
    {
        ((IDisposable)Service).Dispose();
        _provider.Dispose();
    }

    /// <summary>只提供测试宿主接受的固定令牌，不读取或写入真实客户端登录资料。</summary>
    private sealed class DemoTokenStore : ITokenStore
    {
        private string? _token = "template-test-token";
        /// <inheritdoc />
        public bool HasAccessToken => _token is not null;
        /// <inheritdoc />
        public string? GetAccessToken() => _token;
        /// <inheritdoc />
        public void SetAccessToken(string accessToken) => _token = accessToken;
        /// <inheritdoc />
        public void Clear() => _token = null;
    }
}
