using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购详情仅提供独立关联工作区入口，不传递本单或当前采购行。</summary>
public sealed partial class PurchaseOrderDetailViewModel
{
    private bool CanOpenDemandAssociation() => _currentUser.HasPermission(PermissionKeys.ProcurementOrderRead)
        && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit);
    [RelayCommand(CanExecute = nameof(CanOpenDemandAssociation))]
    private Task OpenDemandAssociationAsync() => CanOpenDemandAssociation()
        ? _navigation.OpenAsync(PurchaseDemandAssociationViewModel.RouteKey) : Task.CompletedTask;
}
