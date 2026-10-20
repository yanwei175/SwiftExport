using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Authentication;
using SwiftExportClient.Navigation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>呈现累计创建结果，支持返回准备页修正失败输入及打开实际采购。</summary>
public sealed partial class PurchaseDraftResultViewModel(PurchaseDraftWorkflowViewModel session, INavigationService? navigation = null, ICurrentUserService? user = null) : ViewModelBaseEx
{
    /// <summary>集中采购会话，结果页只读展示其累计结果。</summary>
    public PurchaseDraftWorkflowViewModel Session { get; } = session;

    /// <summary>请求容器返回采购准备页。</summary>
    public event EventHandler? ReturnToPreparationRequested;

    [RelayCommand]
    private void ReturnToPreparation() => ReturnToPreparationRequested?.Invoke(this, EventArgs.Empty);

    private bool CanOpenPurchaseOrder(CreatedPurchaseDraftDto? draft) => navigation is not null && draft is not null && draft.PurchaseOrderId != Guid.Empty
        && user?.HasPermission(PermissionKeys.ProcurementOrderRead) == true;
    /// <summary>按稳定采购身份打开实际创建的草稿，不从展示单号反查。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenPurchaseOrder))]
    private Task OpenPurchaseOrderAsync(CreatedPurchaseDraftDto? draft) => CanOpenPurchaseOrder(draft)
        ? navigation!.OpenDocumentAsync(PurchaseOrderDetailViewModel.RouteKey, $"purchase-order:new:{draft!.PurchaseOrderId:N}", new PurchaseOrderDocumentOpenRequest { PurchaseOrderId = draft.PurchaseOrderId })
        : Task.CompletedTask;
}
