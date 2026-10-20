using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Navigation;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>让选单、管理及详情入口共用一个采购单反馈文档，复用现有导航权限和页签生命周期。</summary>
public static class PurchaseOrderProgressNavigation
{
    /// <summary>生成含反馈语义的稳定文档身份。</summary>
    public static string DocumentKey(Guid order) => $"purchase-order-feedback:{order:N}";

    /// <summary>先由导航校验并打开/激活文档；重复打开只向干净文档提交行定位意图。</summary>
    public static async Task OpenAsync(INavigationService navigation, Guid order, Guid? line = null)
    {
        if (order == Guid.Empty) return;
        var key = DocumentKey(order);
        var existing = navigation.OpenTabs.FirstOrDefault(x => x.RouteKey == PurchaseOrderProgressWorkspaceViewModel.DocumentRouteKey && x.DocumentKey == key);
        var request = new PurchaseOrderProgressOpenRequest { PurchaseOrderId = order, PurchaseOrderLineId = line };
        await navigation.OpenDocumentAsync(PurchaseOrderProgressWorkspaceViewModel.DocumentRouteKey, key, request);
        if (existing is not null && ReferenceEquals(navigation.SelectedTab, existing)
            && existing.ContentViewModel is PurchaseOrderProgressWorkspaceViewModel model)
            model.AcceptOpenRequest(request);
    }
}
