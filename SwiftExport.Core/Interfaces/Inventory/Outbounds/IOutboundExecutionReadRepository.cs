using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory.Outbounds;

/// <summary>读取出库过账与反向校验所需的库存事实，不写余额或流水。</summary>
public interface IOutboundExecutionReadRepository
{
    /// <summary>读取仓库产品当前余额。</summary>
    Task<decimal> ReadBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>读取可用于正向过账的仓库名称。</summary>
    Task<string?> ReadActiveWarehouseNameAsync(Guid warehouseId, CancellationToken token);
    /// <summary>读取原流水及其关联的反向、替代事实。</summary>
    Task<IReadOnlyList<InventoryLedgerEntry>> ReadReverseLedgersAsync(Guid uid, CancellationToken token);
    /// <summary>读取反向所需的完整余额投影。</summary>
    Task<InventoryBalance?> ReadReverseBalanceAsync(Guid warehouseId, Guid productId, CancellationToken token);
    /// <summary>历史反向只要求原仓库存在且未删除。</summary>
    Task<bool> ReverseWarehouseExistsAsync(Guid warehouseId, CancellationToken token);
}
