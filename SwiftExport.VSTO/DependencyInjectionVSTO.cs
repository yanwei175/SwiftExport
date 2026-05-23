using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW;
using System.Windows.Threading;
namespace SwiftExport.VSTO
{
    public static class DependencyInjectionVSTO
    {
        public static IServiceCollection AddVstoServices(this IServiceCollection services, Dispatcher uiDispatcher = null)
        {
            // 注册前端视图层所有依赖
            
            DependencyInjection.AddViewServices(services, uiDispatcher);

            return services;
        }
    }



}
