using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Suppliers.SupplierProducts.Models;

public sealed partial class SupplierProductLineModel : BaseLineModel<SupplierProductDto>
{
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial Guid SupplierId { get; set; }
    [ObservableProperty] public partial string SupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierName { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid SkuId { get; set; }
    [ObservableProperty] public partial string ProductSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    [ObservableProperty] public partial string MaterialType { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierSkuDescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierSkuFeaturesZh { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? UnitPrice { get; set; }
    [ObservableProperty] public partial decimal? TaxPrice { get; set; }
    [ObservableProperty] public partial string Currency { get; set; } = "CNY";
    [ObservableProperty] public partial decimal? TaxRate { get; set; }

    public override string? ValidateRow()
    {
        if (SupplierId == Guid.Empty || string.IsNullOrWhiteSpace(SupplierCode)) return "必须关联供应商。";
        if (SkuId == Guid.Empty || string.IsNullOrWhiteSpace(ProductSku)) return "必须关联标准 SKU。";
        if (string.IsNullOrWhiteSpace(SupplierSku)) return "供应商料号不能为空。";
        if (UnitPrice < 0 || TaxPrice < 0) return "价格不能小于 0。";
        return null;
    }

    protected override void LoadFromDto(SupplierProductDto dto) => dto.Adapt(this);

    public override SupplierProductDto ToDto() => this.Adapt<SupplierProductDto>();
}
