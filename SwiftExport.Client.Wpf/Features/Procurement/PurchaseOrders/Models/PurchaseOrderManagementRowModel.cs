using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购订单管理页的新链头表只读投影。</summary>
public sealed partial class PurchaseOrderManagementRowModel : ObservableObject
{
    /// <summary>当前页批量操作的临时勾选状态，不写入传输契约或数据库。</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>上次批量确认是否出现无法判定写入结果的状态；核实前禁止生成新命令身份。</summary>
    [ObservableProperty]
    public partial bool HasUnknownConfirmationResult { get; private set; }

    /// <summary>当前订单最近一次批量确认状态提示。</summary>
    [ObservableProperty]
    public partial string BatchConfirmationStatus { get; private set; } = string.Empty;

    public Guid PurchaseOrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
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
    public string UnitSnapshot { get; set; } = string.Empty;
    public PurchaseReadyStatusEnum ReadyStatus { get; set; }
    public PurchaseReceiptStatusEnum ReceiptStatus { get; set; }
    public int DueHitLineCount { get; set; }
    public DateTime? EarliestDueHitDate { get; set; }
    public bool HasUnconfirmedReadyRisk { get; set; }
    /// <summary>风险使用可读文字，不只依赖勾选或颜色。</summary>
    public string ReadyRiskText => HasUnconfirmedReadyRisk ? "货好但采购未确认" : string.Empty;
    public string ReviewReason { get; set; } = string.Empty;
    public DateTime? ExpectedCompletionDate { get; set; }
    public int Version { get; set; }
    public bool CanConfirm { get; set; }
    public bool CanCancelConfirmation { get; set; }
    /// <summary>服务端对从未确认整张草稿给出的删除资格。</summary>
    public bool CanDeleteDraft { get; set; }
    /// <summary>无法删除整张草稿的具体原因。</summary>
    public string DeleteDraftBlockReason { get; set; } = string.Empty;
    public string CommandBlockedReason { get; set; } = string.Empty;

    /// <summary>标记确认写入结果未知，要求用户先刷新并核实服务端状态。</summary>
    public void MarkConfirmationResultUnknown()
    {
        HasUnknownConfirmationResult = true;
        BatchConfirmationStatus = "该采购有未决写入；请核实或重试原命令，不会创建新的命令身份。";
    }
    /// <summary>原请求成功或明确拒绝后清理会话投影提示。</summary>
    public void ClearConfirmationResultUnknown() { HasUnknownConfirmationResult = false; BatchConfirmationStatus = string.Empty; }
}
