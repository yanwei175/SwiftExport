using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.AppLayer.DI;
using SwiftExport.UiKongJianFrameWork.AppForms;
using SwiftExport.UiKongJianFrameWork.DI;
using SwiftExportVSTO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
            string databasePath = Properties.Settings.Default.DataBase.ToString();
            // 1. 加载配置
            var configuration = new ConfigurationBuilder()
                                .AddInMemoryCollection(new Dictionary<string, string>
                                {
                                    ["ConnectionStrings:DataBase"] = databasePath,
                                })
                                .Build();

            var services = new ServiceCollection();

            // 注入基础配置
            services.AddSingleton<IConfiguration>(configuration);

            // 公共层
            services.AddAppLayerServers(configuration);
            services.AddUiKongJianFramerWorkServers();
            //services.AddUtility();

            // 日志
            services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            // 本模块服务
            services.AddTransient<LoginFrm>();

            return services.BuildServiceProvider();
        }

        // 兼容 C# 7.3 的快捷获取服务方法
        public static T GetService<T>()
        {
            return Provider.GetRequiredService<T>();
        }

        /// <summary>
        /// VSTO 核心：解决异步回来的跨线程问题
        /// </summary>
        public static void InvokeOnUI(Action action)
        {
            // VSTO 没有单例 MainForm，需要通过 OpenForms 寻找活动的同步上下文
            Form syncForm = Application.OpenForms.Cast<Form>().LastOrDefault();

            if (syncForm != null && syncForm.InvokeRequired)
            {
                syncForm.Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
