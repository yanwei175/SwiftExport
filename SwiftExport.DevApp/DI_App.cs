using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW;
using SwiftExport.DevApp.ViewModels;
using System.Windows.Threading;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// DevApp 启动项依赖注入入口。
    /// </summary>
    public static class DI_App
    {
        public static IServiceCollection AddAppServices(
            this IServiceCollection services,
            Dispatcher uiDispatcher = null)
        {
            // 注册前端视图层所有依赖
            DependencyInjection.AddViewServices(services, uiDispatcher);

            // DevApp 专属：主窗口 + Shell ViewModel
            services.AddSingleton<ShellViewModel>();
            services.AddTransient<MainWindow>();

            return services;
        }
    }
}

