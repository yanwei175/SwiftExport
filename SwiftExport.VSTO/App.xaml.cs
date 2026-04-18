using System.Threading.Tasks;
using System.Windows;

namespace SwiftExport.VSTO
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        private static bool _isInitialized = false;
        public App()
        {
            // 防止 WPF 引擎自动关闭
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }


        /// <summary>
        /// 全局唯一的初始化入口，谁先调用谁负责初始化
        /// </summary>
        public static async Task EnsureApplication()
        {
            if (_isInitialized) return;
            if (Application.Current == null)
            {
                _ = new App();
            }

            // 中文注释：VSTO 宿主下统一注入 Dispatcher，
            // 让 SwiftExport.View.FW 与 MateWpfThemes.FW 都通过同一 UI 线程调度。
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            SwiftExport.View.FW.DispatcherProvider.Initialize(dispatcher);
            MateWpfThemes.FW.DispatcherProvider.Initialize(dispatcher);

            _isInitialized = true;

            // 可以在这里加一个日志，看看是谁触发的
            System.Diagnostics.Debug.WriteLine("WPF 环境初始化完成。");
        }

        private async Task InitializeMDSkinAsync()
        {
            //this.Resources.MergedDictionaries.Clear();

            //// 设置字体 (Microsoft YaHei)
            //// <FontFamily x:Key="MaterialDesignFont">Microsoft YaHei</FontFamily>
            //this.Resources.Add("MaterialDesignFont", new FontFamily("Microsoft YaHei"));
            //// 加载 MD3 默认样式
            //// <ResourceDictionary Source="pack://.../MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
            //this.Resources.MergedDictionaries.Add(new ResourceDictionary
            //{
            //    Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
            //});

            ////加载 BundledTheme (主题配置)
            //// <materialDesign:BundledTheme BaseTheme="Light" PrimaryColor="Teal" SecondaryColor="Lime" />
            //var bundledTheme = new BundledTheme
            //{
            //    BaseTheme = BaseTheme.Light,
            //    PrimaryColor = PrimaryColor.Teal,
            //    SecondaryColor = SecondaryColor.Lime
            //};
            //this.Resources.MergedDictionaries.Add(bundledTheme);
        }


    }
}
