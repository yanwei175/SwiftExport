using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
/// <summary>采购反向原因及影响展示，按钮资格来自服务端预览与有效原因。</summary>
public sealed partial class ProcurementReversalPreviewViewModel(ProcurementReversalPreviewDto preview) : ObservableObject, IDialogContentState
{
    /// <summary>本次服务端预览，不能当作已经执行。</summary>
    public ProcurementReversalPreviewDto Preview { get; } = preview;
    /// <summary>明确取消确认保留承接，删行及作废只解除预览列出的本单贡献，阻断仍以服务端为准。</summary>
    public string ImpactDescription => Preview.Operation == SwiftExport.Contracts.SysEnums.ProcurementReversalOperation.CancelConfirmation
        ? "取消确认只恢复采购草稿，保留全部需求承接及贡献，不会释放物料需求容量。"
        : Preview.Operation == SwiftExport.Contracts.SysEnums.ProcurementReversalOperation.DeleteDraft
            ? "删除整张从未确认的采购草稿，并释放下方全部来源占用；有确认历史或下游历史时仍被阻断，应按资格使用作废。"
            : "仅解除下方列出的本采购行需求贡献，保留历史；收货、交付或其他有效执行仍可能阻断。物料需求回退须另行执行。";
    [ObservableProperty] public partial string Reason { get; set; } = string.Empty;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => Preview.CanExecute && Preview.Blockers.Count == 0 && !string.IsNullOrWhiteSpace(Reason) && Reason.Trim().Length <= 500;
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc/>
    public event EventHandler? StateChanged;
    partial void OnReasonChanged(string value) => StateChanged?.Invoke(this, EventArgs.Empty);
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;
}
