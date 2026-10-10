using CommunityToolkit.Mvvm.ComponentModel;
using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.Sales.Orders.Models;

/// <summary>承载销售订单 Detail 头部的可编辑状态和业务快照。</summary>
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
    /// <summary>保留查询得到的废弃汇率快照，不提供编辑入口，不参与本地校验。</summary>
    [ObservableProperty] public partial decimal? ExchangeRate { get; set; }
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

    /// <summary>载入销售单头快照，包括仅供兼容的历史字段。</summary>
    public void Load(SalesOrderEditHeaderDto dto) => dto.Adapt(this);

    /// <summary>输出销售单头编辑快照；后端忽略废弃汇率的输入。</summary>
    public SalesOrderEditHeaderDto ToDto() => this.Adapt<SalesOrderEditHeaderDto>();

    /// <summary>验证客户、订单日期和定金比例，汇率缺失或旧非法值不阻断操作。</summary>
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

        if (DepositRate is < 0 or > 1)
        {
            return "定金比例必须在 0 到 1 之间。";
        }

        return null;
    }
}
