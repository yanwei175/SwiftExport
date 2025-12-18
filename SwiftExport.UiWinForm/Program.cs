using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.AppLayer.DI;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.AppLayer.Services;
using SwiftExport.UiKongJianFrameWork.AppForms;

namespace SwiftExport.UiWinForm
{
    internal static class Program
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.          
            ApplicationConfiguration.Initialize();

            //构建configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // 注入 DI
            var services = new ServiceCollection();
            //注册业务服务
            services.AddApplicationServers(configuration);
            //注册窗体
            services.AddTransient<LoginFrm>();
            services.AddTransient<MainFrm>();
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddTransient<FrmCustomersManager>();
            services.AddTransient<FrmSuppliersManager>();

            ServiceProvider = services.BuildServiceProvider();

            using (ServiceProvider as IDisposable)
            {
                //// 显示登录窗体作为对话框
                //var loginForm = ServiceProvider.GetRequiredService<LoginFrm>();
                //if (loginForm.ShowDialog() == DialogResult.OK)
                //{
                // 登录成功后启动主窗体
                var mainForm = ServiceProvider.GetRequiredService<MainFrm>();
                Application.Run(mainForm);
                //}
            }

            //Application.Run(new LoginFrm(_serviceProvider));
        }

        private static void 注入DI()
        {
            {
                //// 由容器解析窗体（自动注入依赖）
                //var loginForm = _serviceProvider.GetRequiredService<LoginFrm>();
                //var services = new ServiceCollection();

                // 注册业务服务
                //services.AddSingleton<IUserService, UserService>();
                //services.AddTransient<IExportService, ExportService>();

                //// 注册窗体
                //services.AddTransient<LoginFrm>();
                //services.AddTransient<MainFrm>();

                //// 构建 ServiceProvider
                //_serviceProvider = services.BuildServiceProvider();

            }
        }

    }
}