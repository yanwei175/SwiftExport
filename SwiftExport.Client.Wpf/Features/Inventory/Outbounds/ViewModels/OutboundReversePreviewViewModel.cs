using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
/// <summary>只读库存恢复预览及独立反过账原因，阻断或空白时不能确认。</summary>
public sealed partial class OutboundReversePreviewViewModel(OutboundReversePreviewDto preview) : ObservableObject, IDialogContentState
{
    /// <summary>服务端原事实预览，不允许编辑数量。</summary>
    public OutboundReversePreviewDto Preview { get; } = preview;
    /// <summary>用户本次反过账原因，不复用原出库用途。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    public partial string Reason { get; set; } = string.Empty;
    partial void OnReasonChanged(string value) => StateChanged?.Invoke(this, EventArgs.Empty);
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => Preview.CanReverse && Preview.Blockers.Count == 0 && !string.IsNullOrWhiteSpace(Reason) && Reason.Length <= 500;
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc/>
    public event EventHandler? StateChanged;
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;
}
