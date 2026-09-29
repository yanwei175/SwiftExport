using System.Windows;

namespace SwiftExport.Client.VSTO
{
    /// <summary>VSTO 的 WPF 资源承载，不创建 Main、StartupUri 或独立消息循环。</summary>
    public partial class App : Application
    {
        /// <summary>手动构造时显式加载 App.xaml；主题服务随后由 WpfRuntimeHost 初始化。</summary>
        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InitializeComponent();
        }
    }
}
