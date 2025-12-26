using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.AppLayer.DI;
using SwiftExport.UiKongJianFrameWork.AppForms;
using SwiftExport.UiKongJianFrameWork.DI;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.UiWinForm.DI
{
    public static class DI_WinFormHost
    {
        private static readonly Lock _lock = new();
        private static IServiceProvider? _provider;

        public static IServiceProvider Provider
        {
            get
            {
                if (_provider != null)
                {
                    return _provider;
                }
                lock (_lock)
                { 
                    _provider ??= BuildProvider();
                }
                return _provider;
            }
        }
        private static ServiceProvider BuildProvider()
        {
            var configuration = new ConfigurationBuilder()
               .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
               .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
               .Build();

            var services = new ServiceCollection();

            // 公共层
            services.AddAppLayerServers(configuration);
            services.AddUiKongJianFramerWorkServers();
            //services.AddUtility();

            // 日志
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            // WinForm UI 层
            services.AddTransient<LoginFrm>();
            services.AddSingleton<MainFrm>();

            return services.BuildServiceProvider();
        }

        // 快捷获取服务的方法，省去在外面写 Provider.GetService
        public static T GetService<T>() where T : notnull => Provider.GetRequiredService<T>();
    }
}


