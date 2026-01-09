using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.AppLayer.DI;
using SwiftExport.UI.Framework.DI;
using System;

namespace SwiftExportAddIn.DI
{
    public static class DI_VstoHost
    {
        private static readonly object _lock = new object();
        private static IServiceProvider _provider;

        public static IServiceProvider Provider
        {
            get
            {
                if (_provider == null)
                {
                    lock (_lock)
                    {
                        if (_provider == null)
                        {
                            _provider = BuildProvider();
                        }
                    }
                }
                return _provider;
            }
        }

        private static IServiceProvider BuildProvider()
        {
            //string databasePath = Properties.Settings.Default.DataBase.ToString();
            //// 1. 加载配置
            ////var configuration = new ConfigurationBuilder()
            ////                    .AddInMemoryCollection(new Dictionary<string, string>
            ////                    {
            ////                        ["ConnectionStrings:DataBase"] = databasePath,
            ////                    })
            ////                    .Build();
            //var configuration = AppConfig.Configuration;

            var services = new ServiceCollection();

            // 注入基础配置
            services.AddSingleton<IConfiguration>(AppConfig.Configuration);

            // 公共层
            services.AddAppLayerServers(AppConfig.Configuration);
            services.AddUIFrameworkServers();
            //services.AddUtility();

            // 日志
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            // 本模块服务


            return services.BuildServiceProvider();
        }

        // 兼容 C# 7.3 的快捷获取服务方法
        public static T GetService<T>()
        {
            return Provider.GetRequiredService<T>();
        }

    }
}
