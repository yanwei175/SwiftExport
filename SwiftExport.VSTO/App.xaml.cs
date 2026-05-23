using System;
using System.Windows;

namespace SwiftExport.VSTO
{
    /// <summary>
    /// App.xaml 的交互逻辑。
    /// 职责：确保 WPF Application 实例存在（触发主题资源字典加载），并禁止 WPF 在窗体关闭时自动 Shutdown。
    /// DI 注册、服务初始化等逻辑统一由 ThisAddIn 负责。
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // VSTO 宿主无主窗口，必须显式关闭，防止窗体关闭时 WPF 自动 Shutdown 导致后续弹窗失败
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        /// <summary>
        /// 确保 WPF Application 实例存在，触发 App.xaml 中主题资源字典的加载。
        /// </summary>
        public static void EnsureApplication()
        {
            if (Current == null)
                _ = new App();

            //手动合并主题资源字典，确保样式生效（VSTO 加载时 App.xaml 中的 MergedDictionaries 可能未被正确处理）
                Current.Resources.Clear();
                var themeDict = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MateWpfThemes.FW;component/Themes/MateThemes.xaml")
                };
                Current.Resources.MergedDictionaries.Add(themeDict);


        }
    }
}
