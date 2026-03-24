using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace SwiftExport.DevApp
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            //注册依赖
            var services = new ServiceCollection();

            services.AddAppServices();

            var provider = services.BuildServiceProvider();
            // 注册到全局
            DiServices.Init(provider);

            var mainWindow = DiServices.GetService<MainWindow>();

            mainWindow.Show();

        }

    }


}
