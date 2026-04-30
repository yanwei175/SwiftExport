using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 采购入库头仓储接口。
/// </summary>
public interface IPurchaseReceiveHeadRepository : IRepository<PurchaseReceiveHead>
{
    /// <summary>
    /// 按入库单号查询。
    /// </summary>
    Task<PurchaseReceiveHead?> GetByReceiveNoAsync(string receiveNo);
}
