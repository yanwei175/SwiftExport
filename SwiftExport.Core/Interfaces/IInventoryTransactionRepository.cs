using SwiftExport.Core.Entities.Orders;

namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 库存交易流水仓储接口。
    /// 重要约束：流水只允许新增（Add / AddRange），不允许 Update。
    /// </summary>
    public interface IInventoryTransactionRepository
    {
        /// <summary>
        /// 新增一条库存流水。
        /// </summary>
        Task<int> AddAsync(InventoryTransaction entity);

        /// <summary>
        /// 批量新增库存流水。
        /// </summary>
        Task<IReadOnlyList<int>> AddRangeAsync(IEnumerable<InventoryTransaction> entities);
    }
}
