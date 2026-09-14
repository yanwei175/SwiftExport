using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>只读过账预览；确认能力来自服务端校验结果，不在客户端推导可收量。</summary>
public sealed partial class InboundPostPreviewViewModel(InboundPostPreviewDto preview) : ObservableObject, IDialogContentState
{
    /// <summary>冻结的预览快照，不构成采购或库存预留。</summary>
    public InboundPostPreviewDto Preview { get; } = preview;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => Preview.CanPost && Preview.Blockers.Count == 0;
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc/>
    public event EventHandler? StateChanged { add { } remove { } }
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;
}
