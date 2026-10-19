using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;

/// <summary>采集整修订回退原因，明确影响范围，不执行业务提交。</summary>
public sealed partial class MaterialDemandRollbackReasonViewModel(string scope) : ObservableObject, IDialogContentState
{
    /// <summary>按成品需求归并后的整修订范围。</summary>
    public string Scope { get; } = scope;
    /// <summary>用户输入的真实回退原因，确认后冻结进原命令。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    public partial string Reason { get; set; } = string.Empty;
    /// <inheritdoc />
    public bool IsPrimaryButtonEnabled => !string.IsNullOrWhiteSpace(Reason) && Reason.Trim().Length <= 500;
    /// <inheritdoc />
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc />
    public event EventHandler? StateChanged;
    /// <inheritdoc />
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;
    partial void OnReasonChanged(string value) => StateChanged?.Invoke(this, EventArgs.Empty);
}
