using Mapster;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mappings
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
