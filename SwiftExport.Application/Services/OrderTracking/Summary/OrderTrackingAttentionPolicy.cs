using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
namespace SwiftExport.Application.Services.OrderTracking.Summary;
/// <summary>关注项白名单和额外权限；筛选与摘要共享维度边界，不把多个条件当作任选权限。</summary>
public static class OrderTrackingAttentionPolicy
{
    /// <summary>识别合法关注码，未知值必须被拒绝。</summary>
    public static bool IsKnown(string code)=>code is "Overdue" or "MissingRequiredDate" or "DeliveryReview" or "NoActiveMaterial"
      or "PurchaseShortfall" or "PurchaseExcess" or "PurchaseReview" or "ReceiptOutstanding" or "ReceiptExcess" or "ReceiptReview" or "SourceReview";
    /// <summary>给出所选关注项需同时具备的权限；基础OrderTracking由入口单独检查。</summary>
    public static IEnumerable<string> RequiredPermissions(IEnumerable<string>? codes)=>(codes??[]).SelectMany(code=>code switch {
        "Overdue" or "DeliveryReview"=>new[]{PermissionKeys.ShippingPackingListView},
        "NoActiveMaterial"=>new[]{PermissionKeys.DemandPlanningMaterialView},
        "PurchaseShortfall" or "PurchaseExcess" or "PurchaseReview"=>new[]{PermissionKeys.DemandPlanningMaterialView,PermissionKeys.ProcurementOrderRead},
        "ReceiptOutstanding" or "ReceiptExcess" or "ReceiptReview"=>new[]{PermissionKeys.DemandPlanningMaterialView,PermissionKeys.ProcurementOrderRead,PermissionKeys.InventoryInboundView},
        _=>Array.Empty<string>()}).Distinct();
}
