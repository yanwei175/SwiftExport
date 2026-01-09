using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;

namespace SwiftExportAddIn.DI
{
    public static class AppConfig
    {
        // 全局只读配置对象
        public static IConfiguration Configuration { get; private set; }

        //获取是否为开发模式
        public static bool IsDevMode => Configuration.GetValue<bool>("RibbonSettings:DevelopmentMode");

        // 静态构造函数，程序一启动就会自动加载
        static AppConfig()
        {
            // 指向真实的 bin/安装 目录
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            string assemblyFolder = Path.GetDirectoryName(path);

            // 2. 构建配置
            var builder = new ConfigurationBuilder()
                .SetBasePath(assemblyFolder)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
        }

        // === 辅助属性：方便代码里直接调用 ===

        // 获取数据库连接字符串
        public static string DbConnectionString => Configuration.GetConnectionString("DataBase");

    }
}
