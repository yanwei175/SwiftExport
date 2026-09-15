using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
/// <summary>采购反向原因及影响展示，按钮资格来自服务端预览与有效原因。</summary>
public sealed partial class ProcurementReversalPreviewViewModel(ProcurementReversalPreviewDto preview) : ObservableObject, IDialogContentState
{
    /// <summary>本次服务端预览，不能当作已经执行。</summary>
    public ProcurementReversalPreviewDto Preview { get; } = preview;
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
