using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.AppLayer.DI;
using SwiftExport.UiKongJianFrameWork.DI;
using System;

namespace SwiftExport.AppHost
{
    public static class AppHostBuilder
    {
        public static IServiceProvider BuildForAddIn(IConfiguration configuration)
        {
            var services = new ServiceCollection();

            // 公共层
            services.AddAppLayerServers(configuration);
            services.AddUiKongJianFramerWorkServers();
            //services.AddUtility();

            //// AddIn 专用
            //services.AddAddInServices();

            return services.BuildServiceProvider();
        }

        public static IServiceProvider BuildForWinForm(IConfiguration configuration)
        {
            var services = new ServiceCollection();

            // 公共层
            services.AddAppLayerServers(configuration);
            services.AddUiKongJianFramerWorkServers();
            //services.AddUiKongJianFramWork();
            //services.AddUtility();

            //// WinForm 专用
            //services.AddUiWinForm();

            return services.BuildServiceProvider();
        }
    }
}
