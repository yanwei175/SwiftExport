using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 采购订单行仓储接口。
/// </summary>
public interface IPurchaseOrderLineRepository : IRepository<PurchaseOrderLine>
{
    Task<List<SupplierDto>> GetAwaitingInboundSuppliersAsync();
    Task<List<PurchaseOrderLineDto>> GetReadOnlyLinesByPurchaseOrderIdAsync(int purchaseOrderId);
}
