using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购订单管理页的新链头表只读投影。</summary>
public sealed partial class PurchaseOrderManagementRowModel
{
    public Guid PurchaseOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime? OrderDate { get; set; }
    public ConfirmationStatusEnum ConfirmationStatus { get; set; }
    public PurchaseOrderLifecycleStatusEnum LifecycleStatus { get; set; }
    public string BoNo { get; set; } = string.Empty;
    public DateTime? ExpectedDate { get; set; }
    public DateTime? ReplyDate { get; set; }
    public int? LineCount { get; set; }
    public decimal? TotalQty { get; set; }
    public decimal? TaxTotalAmount { get; set; }
    public string ReceiptProgress { get; set; } = string.Empty;
    public int? ReportedLineCount { get; set; }
    public decimal? LatestReadyQty { get; set; }
    public DateTime? ExpectedCompletionDate { get; set; }
    public int Version { get; set; }
    public bool CanConfirm { get; set; }
    public bool CanCancelConfirmation { get; set; }
    public string CommandBlockedReason { get; set; } = string.Empty;
}
