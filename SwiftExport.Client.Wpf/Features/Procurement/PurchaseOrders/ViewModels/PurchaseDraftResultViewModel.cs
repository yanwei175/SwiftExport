using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>呈现本次会话累计创建结果，并提供返回只读审核的页面级命令。</summary>
public sealed partial class PurchaseDraftResultViewModel(PurchaseDraftWorkflowViewModel session) : ViewModelBaseEx
{
    /// <summary>集中采购会话，结果页只读展示其累计结果。</summary>
    public PurchaseDraftWorkflowViewModel Session { get; } = session;

    /// <summary>请求容器返回采购审核页。</summary>
    public event EventHandler? ReturnToPreviewRequested;

    [RelayCommand]
    private void ReturnToPreview() => ReturnToPreviewRequested?.Invoke(this, EventArgs.Empty);
}
