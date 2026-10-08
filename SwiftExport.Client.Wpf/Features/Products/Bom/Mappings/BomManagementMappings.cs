using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Models;

namespace SwiftExportClient.Features.Products.Bom.Mappings;

/// <summary>集中配置 BOM 管理只读投影到前端模型的结构转换。</summary>
public sealed class BomManagementMappings : IRegister
{
    /// <summary>归一化可空展示文本，不把保存规则藏进映射。</summary>
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ProductLookupDto, BomLineModel>().IgnoreNonMapped(true)
            .Map(d => d.SubSkuId, s => s.Uid).Map(d => d.SubSku, s => s.Sku ?? string.Empty)
            .Map(d => d.Description, s => string.IsNullOrWhiteSpace(s.DescriptionZh) ? s.DescriptionEn ?? string.Empty : s.DescriptionZh)
            .Map(d => d.Features, s => string.IsNullOrWhiteSpace(s.FeaturesZh) ? s.FeaturesEn ?? string.Empty : s.FeaturesZh)
            .Map(d => d.Finish, s => s.Finish ?? string.Empty).Map(d => d.MainImage, s => s.MainImage ?? string.Empty);
        config.NewConfig<BomProductListItemDto, BomProductItemModel>()
        .Map(d => d.MasterSku, s => s.MasterSku ?? string.Empty)
        .Map(d => d.Sku, s => s.Sku ?? string.Empty)
        .Map(d => d.DescriptionEn, s => s.DescriptionEn ?? string.Empty)
        .Map(d => d.DescriptionZh, s => s.DescriptionZh ?? string.Empty)
        .Map(d => d.MainImage, s => s.MainImage ?? string.Empty)
        .Map(d => d.Finish, s => s.Finish ?? string.Empty)
        .Map(d => d.CategoryName, s => s.CategoryName ?? string.Empty)
        .Map(d => d.SupplyType, s => s.SupplyType ?? string.Empty)
        .Map(d => d.Unit, s => s.Unit ?? string.Empty)
        .Map(d => d.BomNo, s => s.BomNo ?? string.Empty);
    }
}
