using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Routing;
using SwiftExport.View.FW.Services;
using System;
using System.Threading.Tasks;

namespace SwiftExport.VSTO
{
    public partial class ThisAddIn
    {
        // 离线模式开关：true = 不发起任何远程 API 调用（断网/后端未启动时使用），false = 正常联调模式
        private const bool StartupOfflineMode = false;

        private async void ThisAddIn_Startup(object sender, System.EventArgs e)
        {
            await InitializeWpfAsync();
        }

        private void ThisAddIn_Shutdown(object sender, System.EventArgs e)
        {
            // 释放单例持有的资源（如 Excel COM 对象引用）
            DiServices.Reset();
            // 显式关闭 WPF Dispatcher，防止 Excel 退出后进程假死在后台
            System.Windows.Application.Current?.Shutdown();
        }

        /// <summary>
        /// WPF 环境与 DI 容器初始化，职责收拢于此，保持 ThisAddIn_Startup 简洁。
        /// </summary>
        private async Task InitializeWpfAsync()
        {
            // 1. 确保 WPF Application 实例存在，触发 App.xaml 主题资源字典加载
            App.EnsureApplication();

            // 2. 同步离线模式配置（必须在 AddVstoServices 之前设置）
            AppRuntimeOptions.DisableRemoteApi = StartupOfflineMode;

            // 3. 构建 DI 容器（AddVstoServices 内部自动捕获当前 UI 线程 Dispatcher）
            var services = new ServiceCollection();
            services.AddVstoServices();
            var provider = services.BuildServiceProvider();

            // 4. 初始化全局服务定位器
            IocProvider.Init(provider);
            DiServices.Init(provider);

            // 5. 后台预加载字典与路由缓存（不阻塞启动，离线模式跳过）
            if (!AppRuntimeOptions.DisableRemoteApi)
            {
                _ = TryPreloadDictAsync();
                _ = TryPreloadViewRoutesAsync();
            }


  
        }

        /// <summary>
        /// 后台预加载字典缓存，失败仅记录日志，不影响主流程。
        /// </summary>
        private static async Task TryPreloadDictAsync()
        {
            var dictCache = DiServices.GetService<IDictCacheService>();
            if (dictCache == null) return;
            try
            {
                await dictCache.PreloadAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ThisAddIn] Dict preload failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 后台预加载视图路由缓存，失败自动降级到静态 ViewRoutes.cs 兜底。
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
                System.Diagnostics.Debug.WriteLine("[ThisAddIn] ViewRoute preload failed: " + ex.Message);
            }
        }

        #region VSTO 生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}