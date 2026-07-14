using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Customers.CustomerManagement.Models;

public sealed partial class CustomerLineModel : BaseLineModel<CustomerDto>
{
    public string ClientKey { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerName { get; set; } = string.Empty;
    [ObservableProperty] public partial string Address { get; set; } = string.Empty;
    [ObservableProperty] public partial string PhoneNumber { get; set; } = string.Empty;
    [ObservableProperty] public partial string ContactPerson { get; set; } = string.Empty;
    [ObservableProperty] public partial string LabelFolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string ManualFolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string DrawingFolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string LinedrawingFolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string BarcodeFolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string SpecSheetFolderPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string DocRelateNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string IncotermSortRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string Incoterm { get; set; } = string.Empty;
    [ObservableProperty] public partial string IncotermLocation { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal DepositRate { get; set; }
    [ObservableProperty] public partial int LeadTimeDays { get; set; }
    [ObservableProperty] public partial string PaymentMethod { get; set; } = string.Empty;
    [ObservableProperty] public partial string Currency { get; set; } = string.Empty;
    [ObservableProperty] public partial string EtdSortRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string ShippedBy { get; set; } = string.Empty;

    public override string? ValidateRow()
    {
        if (string.IsNullOrWhiteSpace(CustomerCode)) return "客户代码不能为空。";
        if (string.IsNullOrWhiteSpace(CustomerName)) return "客户名称不能为空。";
        if (DepositRate is < 0 or > 1) return "定金比例必须在 0 到 1 之间。";
        if (LeadTimeDays < 0) return "交期不能小于 0。";
        return null;
    }

    protected override void LoadFromDto(CustomerDto dto) => dto.Adapt(this);

    public override CustomerDto ToDto() => this.Adapt<CustomerDto>();
}
