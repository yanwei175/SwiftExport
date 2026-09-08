using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Bootstrapper;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Hosting.Windows;

namespace SwiftExport.Client.VSTO.Ioc
{
    /// <summary>插件唯一服务注册入口，不通过业务页面访问全局容器。</summary>
    public static class ServiceRegistration
    {
        /// <summary>注册认证基础设施；外部传入调度器由 Bootstrapper 释放。</summary>
        public static ServiceProvider Build(ApiOptions options, ExcelDispatcher dispatcher, AddInLog log)
        {
            var services = new ServiceCollection();
            services.AddSingleton(log);
            services.AddSingleton<IUiDispatcher>(dispatcher);
            services.AddSingleton<IExcelDispatcher>(dispatcher);
            services.AddSingleton<ITokenStore>(new ProtectedTokenStore(options.BaseAddress, log));
            services.AddSingleton<SessionService>();
            services.AddSingleton<CurrentUserService>();
            services.AddSingleton(provider => new ApiClientRegistration(options.BaseAddress, provider.GetRequiredService<SessionService>()));
            services.AddSingleton<IAuthGateway>(provider => provider.GetRequiredService<ApiClientRegistration>());
            services.AddSingleton<AuthService>();
            services.AddSingleton<ApiCallRunner>();
            services.AddSingleton<WindowManager>();
            services.AddSingleton<LoginCoordinator>();
            services.AddScoped<LoginViewModel>();
            services.AddScoped<LoginWindow>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }
}
