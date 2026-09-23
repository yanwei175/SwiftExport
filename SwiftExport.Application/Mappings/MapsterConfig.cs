using Mapster;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Mappings
{
    /// <summary>集中注册 Application 层的实体与 DTO 映射。</summary>
    public static class MapsterConfig
    {
        /// <summary>注册服务映射配置，并同步静态 Adapt 使用的全局映射。</summary>
        public static void RegisterMappings(IServiceCollection services)
        {
            var config = new TypeAdapterConfig();
            config.Scan(typeof(MapsterConfig).Assembly);
            RegisterBomMappings(config);
            RegisterBomMappings(TypeAdapterConfig.GlobalSettings);
            services.AddSingleton(config);// 注册 Mapster
        }

        /// <summary>显式映射 BOM 持久化字段与现有 DTO 字段。</summary>
        private static void RegisterBomMappings(TypeAdapterConfig config)
        {
            config.NewConfig<Bom, BomDto>()
                .Map(destination => destination.SubSkuId, source => source.ComponentProductId)
                .Map(destination => destination.SubSku, source => source.ComponentSkuSnapshot);
            config.NewConfig<Bom, BomTreeDto>()
                .Map(destination => destination.SubSkuId, source => source.ComponentProductId)
                .Map(destination => destination.SubSku, source => source.ComponentSkuSnapshot);
            config.NewConfig<BomDto, Bom>()
                .Map(destination => destination.ComponentProductId, source => source.SubSkuId)
                .Map(destination => destination.ComponentSkuSnapshot, source => source.SubSku);
        }
    }
}
