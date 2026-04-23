using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Routing;
using SwiftExport.View.FW.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        // 中文注释：离线模式开关。
        // true  = 启动时不做任何远程 API 调用（适合在家/断网/后端未启动时调前端）
        // false = 正常联调模式
        private const bool StartupOfflineMode = false;
        
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 中文注释：把离线模式同步到 View 层运行时配置（必须在 AddAppServices 之前设置）。
            AppRuntimeOptions.DisableRemoteApi = StartupOfflineMode;

            try
            {
                // 注册依赖。
                // AddAppServices 内部会调用 AddViewServices(Dispatcher.CurrentDispatcher)，
                // 自动完成 DispatcherProvider 初始化，无需在此处手动调用 Initialize。
                var services = new ServiceCollection();
                services.AddAppServices();  // uiDispatcher 留空 = 自动捕获当前 UI 线程 Dispatcher
                var provider = services.BuildServiceProvider();
                
                IocProvider.GetServiceFunc = (type) => provider.GetService(type);// 将 DI 容器提供器传递给控件静态类供控件使用.

                DiServices.Init(provider);

                // 中文注释：先显示主窗体，避免因后端不可用导致启动阶段阻塞，看起来像“程序没弹出来”。
                var mainWindow = DiServices.GetService<MainWindow>();
                mainWindow.Show();

                // 中文注释：字典预加载 + 路由缓存预加载，均后台执行；
                // 离线模式直接跳过，不触发任何 API/数据库链路。
                if (!AppRuntimeOptions.DisableRemoteApi)
                {
                    _ = TryPreloadDictAsync();
                    // 路由缓存预加载：从 DB 拉取 SysViewRouteModel 到内存
                    _ = TryPreloadViewRoutesAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("应用启动失败：" + ex.Message, "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// 中文注释：后台预加载字典，失败仅记录日志，不影响主窗体使用。
        /// </summary>
        private static async Task TryPreloadDictAsync()
        {
            var dictCache = DiServices.GetService<IDictCacheService>();
            if (dictCache == null) return;

            try
            {
                await dictCache.PreloadAsync(new[]
                {
                    DictCodes.SupplyType,
                    DictCodes.Currency,
                    DictCodes.CustomerType,
                    DictCodes.ProductType
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[App] Dict preload failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 后台预加载视图路由缓存（从 DB 的 SysViewRouteModel 表拉取）。
        /// 失败不崩溃，路由将自动降级到静态 ViewRoutes.cs 兜底。
        /// </summary>
        private static async Task TryPreloadViewRoutesAsync()
        {
            var routeService = DiServices.GetService<IViewRouteService>();
            if (routeService == null) return;

            try
            {
                await routeService.RefreshAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[App] ViewRoute preload failed: " + ex.Message);
            }
        }
    }
}
