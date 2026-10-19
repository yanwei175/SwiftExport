using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
/// <summary>采集关闭独立需求的真实原因，只提供弹窗输入状态，不执行关闭命令。</summary>
public sealed partial class GrossDemandCloseReasonViewModel(string sourceNo) : ObservableObject, IDialogContentState
{
    /// <summary>待关闭的独立需求来源单号。</summary>
    public string SourceNo { get; } = sourceNo;
    /// <summary>本次关闭原因，确认时冻结到原关闭命令中。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    public partial string Reason { get; set; } = string.Empty;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => !string.IsNullOrWhiteSpace(Reason) && Reason.Trim().Length <= 500;
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc/>
    public event EventHandler? StateChanged;
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;
    partial void OnReasonChanged(string value) => StateChanged?.Invoke(this, EventArgs.Empty);
}
