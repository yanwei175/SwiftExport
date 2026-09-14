using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>只采集本次删除原因；不承担删除请求和业务状态判断。</summary>
public sealed partial class InboundDeleteDialogViewModel : ObservableObject, IDialogContentState
{
    [ObservableProperty] public partial string Reason { get; set; } = string.Empty;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => !string.IsNullOrWhiteSpace(Reason) && Reason.Length <= 500;
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc/>
    public event EventHandler? StateChanged;
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;
    partial void OnReasonChanged(string value) => StateChanged?.Invoke(this, EventArgs.Empty);
}
