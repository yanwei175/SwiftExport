using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
namespace SwiftExport.VSTO.UserWindow
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
                // 只有这里 new App()，才会加载 App.xaml 里的资源
                var app = new App();
                await app.InitializeMDSkinAsync();
            }
            _isInitialized = true;

            // 可以在这里加一个日志，看看是谁触发的
            System.Diagnostics.Debug.WriteLine("WPF 环境初始化完成。");
        }

        private async Task InitializeMDSkinAsync()
        {
            this.Resources.MergedDictionaries.Clear();

            // 设置字体 (Microsoft YaHei)
            // <FontFamily x:Key="MaterialDesignFont">Microsoft YaHei</FontFamily>
            this.Resources.Add("MaterialDesignFont", new FontFamily("Microsoft YaHei"));
            // 加载 MD3 默认样式
            // <ResourceDictionary Source="pack://.../MaterialDesign3.Defaults.xaml" />
            this.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
            });

            //加载 BundledTheme (主题配置)
            // <materialDesign:BundledTheme BaseTheme="Light" PrimaryColor="Teal" SecondaryColor="Lime" />
            var bundledTheme = new BundledTheme
            {
                BaseTheme = BaseTheme.Light,
                PrimaryColor = PrimaryColor.Teal,
                SecondaryColor = SecondaryColor.Lime
            };
            this.Resources.MergedDictionaries.Add(bundledTheme);
        }


    }
}
