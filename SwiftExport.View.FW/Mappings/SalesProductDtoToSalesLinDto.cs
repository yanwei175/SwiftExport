using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders;

namespace SwiftExport.View.FW.Mappings
{
    /// <summary>
    /// SalesProductDto（产品搜索结果）与 SalesOrderLineDto（订单行 DTO）之间的 Mapster 映射配置。
    /// 主要用于：用户在产品弹窗中选中产品后，将产品信息填充到销售订单新增行中。
    /// </summary>
    public class SalesProductDtoToSalesLinDto : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            RegisterHeaderMapping(config);
        }

        private static void RegisterHeaderMapping(TypeAdapterConfig config)
        {
            // -------------------------------------------------------
            // SalesProductDto → SalesOrderLineDto
            // 场景：用户选产品后，带出产品基本信息到订单行（新行）。
            // -------------------------------------------------------
            config.NewConfig<SalesProductDto, SalesOrderLineDto>()
                // 忽略 Id：目标是订单行 Id，新行应为 0，不能从产品 Id 复制。
                .Ignore(dest => dest.Uid)
                // ProductId 映射产品主键（src.Id 为 ProductDto 的数据库主键）。
                .Map(dest => dest.ProductId, src => src.ProductId)
                .Map(dest => dest.CustomerProductId, src => src.CustomerProductId)
                .Map(dest => dest.LinkSku, src => src.LinkSku)
                .Map(dest => dest.CnyPrice, src => src.CNYPrice)
                .Map(dest => dest.UsdPrice, src => src.USDPrice)
                .Map(dest => dest.PcsOfCarton, src => src.PcsOfCarton)
                .Map(dest => dest.NwOfCarton, src => src.NwOfCarton)
                .Map(dest => dest.GwOfCarton, src => src.GwOfCarton)
                .Map(dest => dest.CbmOfCarton, src => src.CbmOfCarton)
                // 以下同名字段 Mapster 会自动映射，无需显式声明，但注释保留供阅读说明来源：
                // Sku         ← src.Sku
                // LinkSku     ← src.LinkSku
                // MaterialType← src.MaterialType
                // DescriptionEn← src.DescriptionEn
                // FeaturesEn  ← src.FeaturesEn
                // ColorNameEn ← src.ColorNameEn
                // CustomerProductId ← src.CustomerProductId
                // CustomerSku ← src.CustomerSku
                // CNYPrice    ← src.CNYPrice
                // USDPrice    ← src.USDPrice
                // PcsOfCarton ← src.PcsOfCarton
                // NwOfCarton  ← src.NwOfCarton
                // GwOfCarton  ← src.GwOfCarton
                // CbmOfCarton ← src.CbmOfCarton
                // MainImage   ← src.MainImage

                // 以下字段属于订单行独有，不来自产品，忽略（保留默认值）：
                .Ignore(dest => dest.LineNumber)
                .Ignore(dest => dest.SalesOrderId)
                .Ignore(dest => dest.OrderNo)
                .Ignore(dest => dest.Qty)
                .Ignore(dest => dest.CnyAmount)
                .Ignore(dest => dest.UsdAmount)
                .Ignore(dest => dest.Ctns)
                .Ignore(dest => dest.NwSubTotal)
                .Ignore(dest => dest.GwSubTotal)
                .Ignore(dest => dest.CbmSubTotal)
                .Ignore(dest => dest.SubNote)
                .Ignore(dest => dest.LastShippedDate)
                .Ignore(dest => dest.ExpectedDeliveryDate);

            // -------------------------------------------------------
            // SalesOrderLineDto → SalesProductDto（反向，仅映射同名字段）
            // 场景：从已有订单行回填产品选择器时使用。
            // -------------------------------------------------------
            config.NewConfig<SalesOrderLineDto, SalesProductDto>()
                // 将订单行的 ProductId 回填为 SalesProductDto 的主键 Id。
                .Map(dest => dest.Uid, src => src.Uid)
                // ProductId 字段同步回填。
                .Map(dest => dest.ProductId, src => src.ProductId)
                // 其余同名字段（Sku、MaterialType、DescriptionEn 等）由 Mapster 自动映射。
                // 产品侧不存在的字段（LineNumber、QTY 等）会被自动忽略。
                .IgnoreNonMapped(true);
        }
    }
}
