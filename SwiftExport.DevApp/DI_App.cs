using Microsoft.Extensions.DependencyInjection;
using SwiftExport.View.FW;
namespace SwiftExport.DevApp
{

    public static class DI_App
    {
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {

            DI_View.AddViewServices(services);

            services.AddTransient<MainWindow>();



            return services;
        }
    }



}
