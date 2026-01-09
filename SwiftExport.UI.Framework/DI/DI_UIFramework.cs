using Microsoft.Extensions.DependencyInjection;
using SwiftExport.UI.Framework.AppForms;
using SwiftExport.UI.Framework.DialogsForms;
using SwiftExport.UI.Framework.ManagerForms;

namespace SwiftExport.UI.Framework.DI
{
    public static class DI_UIFramework
    {
        /// <summary>
        /// 注册本模块窗体。
        /// </summary>
        public static IServiceCollection AddUIFrameworkServers(this IServiceCollection services)
        {
            // 注册窗体服务
            services.AddTransient<FrmCustomersManager>();
            services.AddTransient<FrmSuppliersManager>();
            services.AddTransient<FrmExcelSheetFieldsMappingManager>();
            services.AddTransient<FrmProductColorManager>();
            services.AddTransient<FrmProductManager>();
            services.AddTransient<FrmWhiteListManager>();
            services.AddTransient<FrmProductSelector>();
            return services;
        }
    }
}