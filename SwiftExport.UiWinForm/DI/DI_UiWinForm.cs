using Microsoft.Extensions.DependencyInjection;

namespace SwiftExport.UiWinForm.DI
{
    public static class DI_UiWinForm
    {
        public static IServiceCollection AddWinFormServers(this IServiceCollection services)
        {
            // 注册窗体服务
            services.AddTransient<LoginFrm>();
            services.AddTransient<MainFrm>();
            return services;
        }
    }
}
