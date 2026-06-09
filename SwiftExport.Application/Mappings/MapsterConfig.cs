using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace SwiftExport.Application.Mappings
{
    public static class MapsterConfig
    {
        public static void RegisterMappings(IServiceCollection services)
        {
            var config = new TypeAdapterConfig();
            config.Scan(typeof(MapsterConfig).Assembly);
            services.AddSingleton(config);// 注册 Mapster
        }
    }
}
