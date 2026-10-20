using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
namespace SwiftExportClient.Features.OrderTracking.Overview.Policies;
/// <summary>只控制客户端关注项候选及摘要显示权限，业务关注判定由服务端负责。</summary>
public static class OrderTrackingAttentionPolicy
{
    private static readonly (string Code, string Label, int Dimension)[] Definitions =
    [
        (OrderTrackingAttentionCodes.Overdue, "逾期", 1), (OrderTrackingAttentionCodes.MissingRequiredDate, "未指定交期", 0),
        (OrderTrackingAttentionCodes.DeliveryReview, "交付待核实", 1), (OrderTrackingAttentionCodes.NoActiveMaterial, "无活动物料", 2),
        (OrderTrackingAttentionCodes.PurchaseShortfall, "已确认采购不足", 3), (OrderTrackingAttentionCodes.PurchaseExcess, "采购占用超量", 3),
        (OrderTrackingAttentionCodes.PurchaseReview, "采购承接待核实", 3), (OrderTrackingAttentionCodes.ReceiptOutstanding, "关联采购整行未收足", 4),
        (OrderTrackingAttentionCodes.ReceiptExcess, "关联采购整行超收", 4), (OrderTrackingAttentionCodes.ReceiptReview, "整行收货待核实", 4),
        (OrderTrackingAttentionCodes.SourceReview, "来源待核实", 0)
    ];
    /// <summary>按精确维度权限生成候选；集合内由后端求并集。</summary>
    public static IReadOnlyList<TrackingFilterOption<string>> GetOptions(ICurrentUserService user)
    {
        if (!user.HasPermission(PermissionKeys.OrderTrackingView)) return [];
        var delivery = user.HasPermission(PermissionKeys.ShippingPackingListView);
        var material = user.HasPermission(PermissionKeys.DemandPlanningMaterialView);
        var purchase = material && user.HasPermission(PermissionKeys.ProcurementOrderRead);
        var receipt = purchase && user.HasPermission(PermissionKeys.InventoryInboundView);
        return Definitions.Where(x => x.Dimension switch { 1 => delivery, 2 => material, 3 => purchase, 4 => receipt, _ => true })
            .Select(x => new TrackingFilterOption<string>(x.Code, x.Label)).ToArray();
    }
    /// <summary>冻结用于本次摘要显示的权限；接纳响应前仍复核当前会话。</summary>
    public static TrackingSummaryAccess GetAccess(ICurrentUserService user) => new(
        user.HasPermission(PermissionKeys.ShippingPackingListView), user.HasPermission(PermissionKeys.DemandPlanningMaterialView),
        user.HasPermission(PermissionKeys.DemandPlanningMaterialView) && user.HasPermission(PermissionKeys.ProcurementOrderRead),
        user.HasPermission(PermissionKeys.DemandPlanningMaterialView) && user.HasPermission(PermissionKeys.ProcurementOrderRead) && user.HasPermission(PermissionKeys.InventoryInboundView),
        GetOptions(user).Select(x => x.Value).ToArray());
    /// <summary>把已授权的稳定关注码显示为中文，未知新码保持中性提示。</summary>
    public static string Label(string code) => Definitions.FirstOrDefault(x => x.Code == code).Label ?? "其他关注项";
}
