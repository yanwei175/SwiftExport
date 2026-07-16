using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.Sales.Orders.Models;

public sealed partial class SalesOrderHeaderEditModel : ObservableObject
{
    [ObservableProperty] public partial Guid Uid { get; set; }
    [ObservableProperty] public partial int Version { get; set; }
    [ObservableProperty] public partial SalesOrderDocumentStatusEnum DocumentStatus { get; set; } = SalesOrderDocumentStatusEnum.Open;
    [ObservableProperty] public partial ConfirmationStatusEnum ConfirmationStatus { get; set; } = ConfirmationStatusEnum.待确认;
    [ObservableProperty] public partial Guid CustomerId { get; set; }
    [ObservableProperty] public partial string CustomerCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string OrderNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string InvoiceNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string CustomerPoNo { get; set; } = string.Empty;
    [ObservableProperty] public partial string ShippedBy { get; set; } = string.Empty;
    [ObservableProperty] public partial string Currency { get; set; } = "USD";
    [ObservableProperty] public partial decimal ExchangeRate { get; set; } = 1m;
    [ObservableProperty] public partial string ShipToName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ShipToAddress { get; set; } = string.Empty;
    [ObservableProperty] public partial string ShipToPhone { get; set; } = string.Empty;
    [ObservableProperty] public partial string Incoterm { get; set; } = string.Empty;
    [ObservableProperty] public partial string IncotermSortRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string IncotermLocation { get; set; } = string.Empty;
    [ObservableProperty] public partial decimal DepositRate { get; set; }
    [ObservableProperty] public partial int LeadTimeDays { get; set; }
    [ObservableProperty] public partial string PaymentMethod { get; set; } = string.Empty;
    [ObservableProperty] public partial string SoFolder { get; set; } = string.Empty;
    [ObservableProperty] public partial string Remark { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime? OrderDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial DateTime? DeliveryDate { get; set; }
    [ObservableProperty] public partial DateTime? ReplyDate { get; set; }
    [ObservableProperty] public partial DateTime? Eta { get; set; }
    [ObservableProperty] public partial DateTime? Etd { get; set; }

    public void Load(SalesOrderEditHeaderDto dto) => dto.Adapt(this);

    public SalesOrderEditHeaderDto ToDto() => this.Adapt<SalesOrderEditHeaderDto>();

    public string? ValidateModel()
    {
        if (CustomerId == Guid.Empty || string.IsNullOrWhiteSpace(CustomerCode))
        {
            return "请选择客户。";
        }

        if (OrderDate is null)
        {
            return "订单日期不能为空。";
        }

        if (ExchangeRate <= 0)
        {
            return "汇率必须大于零。";
        }

        if (DepositRate is < 0 or > 1)
        {
            return "定金比例必须在 0 到 1 之间。";
        }

        return null;
    }
}
