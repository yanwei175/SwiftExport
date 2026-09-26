using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>产品列表可编辑行，保存当前可空关联及独立制造商型号。</summary>
public partial class ProductLineModel : BaseLineModel<ProductDto>
{
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string Finish { get; set; } = string.Empty;
    [ObservableProperty] public partial string MaterialType { get; set; } = string.Empty;
    [ObservableProperty] public partial string Unit { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplyType { get; set; } = string.Empty;
    [ObservableProperty] public partial int BomVersion { get; set; }
    [ObservableProperty] public partial string BomNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string Note { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal CnyPrice { get; set; }
    [ObservableProperty] public partial decimal UsdPrice { get; set; }
    [ObservableProperty] public partial decimal? PcsOfCarton { get; set; }
    [ObservableProperty] public partial decimal? NwOfCarton { get; set; }
    [ObservableProperty] public partial decimal? GwOfCarton { get; set; }
    [ObservableProperty] public partial decimal? CbmOfCarton { get; set; }
    [ObservableProperty] public partial Guid CategoryId { get; set; }
    [ObservableProperty] public partial Guid ColorId { get; set; }
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierName { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? DefaultSupplierId { get; set; }
    /// <summary>产品建议仓库标识；编辑该值不改变入出库单头的实际仓库。</summary>
    [ObservableProperty] public partial Guid? DefaultWarehouseSiteId { get; set; }
    [ObservableProperty] public partial string DefaultWarehouseCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultWarehouseName { get; set; } = string.Empty;
    /// <summary>制造商独立于默认采购供应商。</summary>
    [ObservableProperty] public partial Guid? ManufacturerId { get; set; }
    [ObservableProperty] public partial string ManufacturerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string ManufacturerName { get; set; } = string.Empty;
    /// <summary>允许独立输入的制造商型号。</summary>
    [ObservableProperty] public partial string ManufacturerModel { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultSupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;

    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku)) return Task.FromResult<string?>("SKU 不能为空。");
        if (BomVersion < 0) return Task.FromResult<string?>("BOM 版本不能小于 0。");
        if (CnyPrice < 0 || UsdPrice < 0) return Task.FromResult<string?>("价格不能小于 0。");
        if (DefaultWarehouseSiteId == Guid.Empty || ManufacturerId == Guid.Empty)
            return Task.FromResult<string?>("默认仓库或制造商标识不能是空 Guid。");
        if (ManufacturerModel?.Trim().Length > 200)
            return Task.FromResult<string?>("制造商型号不能超过 200 个字符。");
        return Task.FromResult<string?>(null);
    }

    protected override void LoadFromDto(ProductDto dto)
    {
        dto.Adapt(this);
    }

    public override ProductDto ToDto() => this.Adapt<ProductDto>();
}
