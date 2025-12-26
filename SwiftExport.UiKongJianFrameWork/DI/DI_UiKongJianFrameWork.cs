using Microsoft.Extensions.DependencyInjection;
using SwiftExport.UiKongJianFrameWork.AppForms;

namespace SwiftExport.UiKongJianFrameWork.DI
{
    public static class DI_UiKongJianFrameWork
    {
        /// <summary>
        /// 注册本模块窗体。
        /// </summary>
        public static IServiceCollection AddUiKongJianFramerWorkServers(this IServiceCollection services)
        {
            // 注册窗体服务
            services.AddTransient<FrmCustomersManager>();
            services.AddTransient<FrmSuppliersManager>();
            services.AddTransient<FrmExcelSheetFieldsMappingManager>();
            services.AddTransient<FrmProductColorManager>();
            return services;
        }
    }
}