using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW.Mappings;
using System.Windows.Threading;

namespace SwiftExport.View.FW
{
    /// <summary>
    /// 前端视图层依赖注入总入口（partial class，各职责在独立文件中注册）。
    ///
    /// 文件结构：
    ///   DependencyInjection.cs          ← 本文件，总入口，依次调用各分组注册
    ///   DependencyInjection.Apis.cs     ← Refit HTTP API 接口注册
    ///   DependencyInjection.Services.cs ← 公共前端服务（弹窗、字典缓存等）
    ///   DependencyInjection.Routing.cs  ← 视图路由缓存服务注册
    ///   DependencyInjection.Pages.cs    ← ViewModel + View 成对注册（按业务模块分组）
    ///
    /// 使用方式：
    ///   // WPF 宿主（Dispatcher 自动捕获，无需手动传入）：
    ///   services.AddViewServices();
    ///
    ///   // VSTO 宿主（WPF Dispatcher 来自隐藏窗口，需显式传入）：
    ///   var win = new HiddenWpfWindow();
    ///   services.AddViewServices(win.Dispatcher);
    ///
    /// 新增页面时：只需在 DependencyInjection.Pages.cs 末尾加两行，其他文件不动。
    /// 新增 API 时：只需在 DependencyInjection.Apis.cs 末尾加一行。
    /// </summary>
    public static partial class DependencyInjection
    {
        /// <summary>
        /// 注册所有前端视图层依赖（总入口）。
        ///
        /// <param name="services">DI 容器</param>
        /// <param name="uiDispatcher">
        ///   UI 线程 Dispatcher。
        ///   WPF 场景：留空，自动捕获 Dispatcher.CurrentDispatcher（前提：在 UI 线程调用）。
        ///   VSTO 场景：传入 HiddenWpfWindow.Dispatcher。
        /// </param>
        /// </summary>
        public static IServiceCollection AddViewServices(
            this IServiceCollection services,
            Dispatcher uiDispatcher = null)
        {
            // ---- 初始化 UI 线程 Dispatcher ----
            // AddViewServices 必须在 UI 线程调用（WPF OnStartup / VSTO ThisAddIn_Startup），
            // 因此 Dispatcher.CurrentDispatcher 在此处可安全捕获。
            // VSTO 场景需显式传入，因为 VSTO 的 UI 线程 Dispatcher 来自隐藏 WPF 窗口。
            var dispatcher = uiDispatcher ?? Dispatcher.CurrentDispatcher;

            // 同时初始化业务层与控件库的 DispatcherProvider，保证跨宿主兼容
            DispatcherProvider.Initialize(dispatcher);
            MateWpfThemes.FW.DispatcherProvider.Initialize(dispatcher);

            // 1. Mapster 对象映射配置（必须最先注册）
            MapsterConfig.RegisterMappings(services);

            // 2. 公共前端服务（弹窗、字典缓存等）
            services.AddViewCoreServices();

            // 3. 视图路由缓存服务
            services.AddViewRouting();

            // 4. HTTP 拦截器 + Refit API 接口
            services.AddViewApis();

            // 5. ViewModel + View 页面注册
            services.AddViewPages();

            return services;
        }
    }
}
