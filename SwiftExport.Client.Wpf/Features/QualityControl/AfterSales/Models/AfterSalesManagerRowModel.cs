using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.QualityControl.AfterSales.Models;

/// <summary>售后管理列表的纯展示行，显式区分生命周期、确认及费用未定。</summary>
public sealed class AfterSalesManagerRowModel
{
    public Guid Uid { get; set; }
    public int Version { get; set; }
    /// <summary>单头客户投诉单号，允许为空及重复。</summary>
    public string? CustomerComplaintNo { get; set; }
    public string AfterSalesNo { get; set; } = string.Empty;
    public string CustomerDisplay { get; set; } = string.Empty;
    public DateTime AcceptedDate { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string IssueModels { get; set; } = string.Empty;
    public string ResponsibleSuppliers { get; set; } = string.Empty;
    public DocumentStatusEnum DocumentStatus { get; set; }
    public ConfirmationStatusEnum ConfirmationStatus { get; set; }
    public string DocumentStatusText { get; set; } = string.Empty;
    public string ConfirmationStatusText { get; set; } = string.Empty;
    public decimal KnownAmountCny { get; set; }
    public bool HasUndeterminedAmount { get; set; }
    public string AmountText { get; set; } = string.Empty;
}
