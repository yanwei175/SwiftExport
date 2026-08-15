using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Customers.CustomerProducts.Models;

public sealed partial class CustomerProductLineModel : BaseLineModel<CustomerProductDto>
{
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial Guid CustomerId { get; set; }
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial Guid? SkuId { get; set; }
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinkSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string FeaturesEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    [ObservableProperty] public partial string MaterialType { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPdDescription { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPdSeries { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerColorName { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPdFeatures { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerBarcode1 { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerBarcode2 { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? UsdPrice { get; set; }
    [ObservableProperty] public partial decimal? CnyPrice { get; set; }
    [ObservableProperty] public partial int? LeadTimeDays { get; set; }
    [ObservableProperty] public partial int? Moq { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; } = true;
    [ObservableProperty] public partial string Note { get; set; } = string.Empty;

    public override string? ValidateRow()
    {
        if (CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(CustomerCode)) return "必须关联客户。";
        if (string.IsNullOrWhiteSpace(CustomerSku)) return "客户料号不能为空。";
        if (SkuId.HasValue && string.IsNullOrWhiteSpace(Sku)) return "标准 SKU 关联不完整。";
        if (UsdPrice < 0 || CnyPrice < 0) return "价格不能小于 0。";
        if (LeadTimeDays < 0 || Moq < 0) return "交期和 MOQ 不能小于 0。";
        return null;
    }

    protected override void LoadFromDto(CustomerProductDto dto) => dto.Adapt(this);

    public override CustomerProductDto ToDto() => this.Adapt<CustomerProductDto>();
}
