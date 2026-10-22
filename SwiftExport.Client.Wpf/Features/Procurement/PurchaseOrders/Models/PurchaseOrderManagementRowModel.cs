using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
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

    /// <summary>反向操作已成功但尚未接纳权威重查；旧行不能继续发起新的业务命令。</summary>
    [ObservableProperty]
    public partial bool RequiresRefresh { get; private set; }

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
    public string UnitSnapshot { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool CanConfirm { get; set; }
    public bool CanCancelConfirmation { get; set; }
    /// <summary>服务端对从未确认整张草稿给出的删除资格。</summary>
    public bool CanDeleteDraft { get; set; }
    /// <summary>无法删除整张草稿的具体原因。</summary>
    public string DeleteDraftBlockReason { get; set; } = string.Empty;
    public string CommandBlockedReason { get; set; } = string.Empty;

    /// <summary>立即展示成功回执并废弃全部旧资格；只有分页重查创建的新行才能重新开放操作。</summary>
    public void AcceptReversalResult(ProcurementOrderCommandResult result)
    {
        Version = result.Version;
        ConfirmationStatus = result.ConfirmationStatus;
        LifecycleStatus = result.LifecycleStatus;
        CanConfirm = CanCancelConfirmation = CanDeleteDraft = false;
        DeleteDraftBlockReason = CommandBlockedReason = "采购操作已成功，请刷新核实后继续操作。";
        RequiresRefresh = true;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>标记确认写入结果未知，要求用户先刷新并核实服务端状态。</summary>
    public void MarkConfirmationResultUnknown()
    {
        HasUnknownConfirmationResult = true;
        BatchConfirmationStatus = "该采购有未决写入；请核实或重试原命令，不会创建新的命令身份。";
    }
    /// <summary>原请求成功或明确拒绝后清理会话投影提示。</summary>
    public void ClearConfirmationResultUnknown() { HasUnknownConfirmationResult = false; BatchConfirmationStatus = string.Empty; }
}
