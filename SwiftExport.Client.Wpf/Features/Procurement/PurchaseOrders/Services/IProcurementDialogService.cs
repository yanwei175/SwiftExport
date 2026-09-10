using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

public interface IProcurementDialogService
{
    /// <summary>在单个弹窗内完成物料选择、采购预览与创建，并返回实际应用结果。</summary>
    Task<PurchaseOrderFromMaterialResult> ShowFromMaterialAsync(CancellationToken cancellationToken = default);
}
