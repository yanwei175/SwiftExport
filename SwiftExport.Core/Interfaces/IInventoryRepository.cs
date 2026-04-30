using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 库存仓储接口。
/// </summary>
public interface IInventoryRepository : IRepository<Inventory>
{
    /// <summary>
    /// 按 仓库+库位+SKU 查询库存记录。
    /// </summary>
    Task<Inventory?> GetByWhseLocationSkuAsync(string whseCode, string locationCode, string sku);
}
