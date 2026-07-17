using SwiftExport.Contracts.DTOs.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 采购订单行仓储接口。
/// </summary>
public interface IPurchaseOrderLineRepository : IRepository<PurchaseOrderLine>
{
    Task<List<SupplierDto>> GetAwaitingInboundSuppliersAsync();
    Task<List<PurchaseOrderLineDto>> GetReadOnlyLinesByPurchaseOrderIdAsync(Guid purchaseOrderId);

    /// <summary>锁定采购头下的活动行键范围并取得当前最大行号。</summary>
    Task<int> GetMaxLineNumberForUpdateAsync(Guid purchaseOrderId);

    /// <summary>显式批量插入采购草稿行，保留 Application 预生成的 Uid。</summary>
    Task<int> InsertPurchaseDraftLinesAsync(IEnumerable<PurchaseOrderLine> lines);

    Task<IReadOnlyList<PurchaseOrderLine>> GetDraftLinesForUpdateAsync(Guid purchaseOrderId);

    Task<int> UpdateDraftLineQuantitiesAsync(
        IReadOnlyDictionary<Guid, decimal> quantities,
        string reason);
}
