using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory.Inbounds;

/// <summary>入库执行所需库存、仓库与原流水只读事实持久化边界。</summary>
public interface IInboundExecutionReadRepository
{
    /// <summary>检查异常正式流水，防止状态损坏时删除正式事实。</summary>
    Task<bool> HasLedgerAsync(Guid uid, CancellationToken token);
    /// <summary>只读取得同仓产品余额，不预占。</summary>
    Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>只读校验仓库有效并返回当前名称；无效返回空。</summary>
    Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token);
    /// <summary>读取原单及相关反向、替代流水，异常事实不能被过滤掩盖。</summary>
    Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token);
    /// <summary>只读读取原余额，含已删除投影以显式阻断异常。</summary>
    Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>历史反向允许停用仓，但不允许缺失或已删除仓库。</summary>
    Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token);
}
