using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Suppliers.SupplierManagement.Models;

public sealed partial class SupplierLineModel : BaseLineModel<SupplierDto>
{
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial string SupplierCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplierName { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal? Discount { get; set; }
    [ObservableProperty] public partial string DiscountRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string DocRelateNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string ContactPerson { get; set; } = string.Empty;
    [ObservableProperty] public partial string ContactEmail { get; set; } = string.Empty;
    [ObservableProperty] public partial string ContactPhone { get; set; } = string.Empty;
    [ObservableProperty] public partial string Address { get; set; } = string.Empty;
    [ObservableProperty] public partial string Currency { get; set; } = "CNY";
    [ObservableProperty] public partial decimal? TaxRate { get; set; }
    [ObservableProperty] public partial string PaymentTerms { get; set; } = string.Empty;
    [ObservableProperty] public partial int? DefaultLeadTimeDays { get; set; }

    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(SupplierCode)) return "供应商代码不能为空。";
        if (string.IsNullOrWhiteSpace(SupplierName)) return "供应商名称不能为空。";
        if (string.IsNullOrWhiteSpace(DocRelateNo)) return "单据关联号不能为空。";
        if (Discount is < 0 or > 1) return "折扣必须在 0 到 1 之间。";
        if (TaxRate is < 0 or > 1) return "税率必须在 0 到 1 之间。";
        if (DefaultLeadTimeDays < 0) return "默认交期不能小于 0。";
        return null;
    }

    protected override void LoadFromDto(SupplierDto dto) => dto.Adapt(this);

    public override SupplierDto ToDto() => this.Adapt<SupplierDto>();
}
