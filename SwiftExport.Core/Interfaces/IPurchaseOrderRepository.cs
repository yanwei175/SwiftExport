using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 采购订单头仓储接口。
/// </summary>
public interface IPurchaseOrderRepository : IRepository<PurchaseOrderHead>
{
    Task<PurchaseOrderHead?> GetByBoNoAsync(string boNo, bool withUpdLock = false);
    Task<PurchaseOrderHead?> GetByOrderNoAsync(string orderNo, bool withUpdLock = false);
}
