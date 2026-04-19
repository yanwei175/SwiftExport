using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.View.FW;
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

            // 中文注释：同时初始化业务层与控件库的 DispatcherProvider，
            // 以兼容 WPF 主程序与后续可能的宿主切换（例如 VSTO）。
            SwiftExport.View.FW.DispatcherProvider.Initialize(Dispatcher.CurrentDispatcher);
            MateWpfThemes.FW.DispatcherProvider.Initialize(Dispatcher.CurrentDispatcher);

            try
            {
                // 中文注释：把离线模式同步到 View 层运行时配置。
                AppRuntimeOptions.DisableRemoteApi = StartupOfflineMode;

                // 注册依赖
                var services = new ServiceCollection();
                services.AddAppServices();

                var provider = services.BuildServiceProvider();
                DiServices.Init(provider);

                // 中文注释：先显示主窗体，避免因后端不可用导致启动阶段阻塞，看起来像“程序没弹出来”。
                var mainWindow = DiServices.GetService<MainWindow>();
                mainWindow.Show();

                // 中文注释：字典预加载改为后台执行；离线模式直接跳过，不触发任何 API/数据库链路。
                if (!AppRuntimeOptions.DisableRemoteApi)
                {
                    _ = TryPreloadDictAsync();
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
    }
}
