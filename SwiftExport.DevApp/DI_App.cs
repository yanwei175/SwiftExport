using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW;
using System.Windows.Threading;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// DevApp 启动项依赖注入入口。
    ///
    /// 职责：
    ///   在前端视图层注册（AddViewServices）的基础上，
    ///   额外注册 DevApp 专属的组件（如主窗口）。
    ///
    /// 后续切换为 VSTO 启动项时，新建同名扩展方法即可，View 层注册完全复用：
    ///   services.AddAppServices(hiddenWpfWindow.Dispatcher);
    /// </summary>
    public static class DI_App
    {
        /// <summary>
        /// 注册 DevApp 全部依赖（视图层 + 主窗口）。
        /// 在 App.xaml.cs 的 OnStartup 中调用。
        ///
        /// <param name="services">DI 容器</param>
        /// <param name="uiDispatcher">
        ///   UI 线程 Dispatcher，透传给 AddViewServices。
        ///   WPF 场景留空（自动捕获），VSTO 场景显式传入。
        /// </param>
        /// </summary>
        public static IServiceCollection AddAppServices(
            this IServiceCollection services,
            Dispatcher uiDispatcher = null)
        {
            // 注册前端视图层所有依赖，同时完成 DispatcherProvider 初始化
            DependencyInjection.AddViewServices(services, uiDispatcher);

            // DevApp 专属：主窗口
            services.AddTransient<MainWindow>();

            return services;
        }
    }
}

