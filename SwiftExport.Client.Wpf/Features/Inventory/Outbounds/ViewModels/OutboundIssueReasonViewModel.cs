using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Outbounds.ViewModels;

/// <summary>保存草稿前的独立用途输入，只采集原因，不推导或执行库存动作。</summary>
public sealed partial class OutboundIssueReasonViewModel(string documentNo) : ObservableObject, IDialogContentState
{
    /// <summary>当前补填原因的单据标识。</summary>
    public string DocumentNo { get; } = documentNo;
    /// <summary>用户填写的出库用途，空白与超长值不允许确认。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    public partial string Reason { get; set; } = string.Empty;
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
