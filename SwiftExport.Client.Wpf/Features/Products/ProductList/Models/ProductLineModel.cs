using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Products.ProductList.Models;

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
    [ObservableProperty] public partial string DefaultCustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultCustomerSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultSupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultSupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;

    public override Task<string?> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Sku)) return Task.FromResult<string?>("SKU 不能为空。");
        if (BomVersion < 0) return Task.FromResult<string?>("BOM 版本不能小于 0。");
        if (CnyPrice < 0 || UsdPrice < 0) return Task.FromResult<string?>("价格不能小于 0。");
        return Task.FromResult<string?>(null);
    }

    protected override void LoadFromDto(ProductDto dto)
    {
        dto.Adapt(this);
    }

    public override ProductDto ToDto() => this.Adapt<ProductDto>();
}
