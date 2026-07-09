namespace SwiftExport.Core.Interfaces
{
    /// <summary>
    /// 库存交易流水仓储接口。
    /// 重要约束：流水只允许新增（Create / AddRange），不允许 Update。
    /// </summary>
    public interface IInventoryTransactionRepository
    {
        /// <summary>
        /// 新增一条库存流水。
        /// </summary>

        /// <summary>
        /// 批量新增库存流水。
        /// </summary>
    }
}
