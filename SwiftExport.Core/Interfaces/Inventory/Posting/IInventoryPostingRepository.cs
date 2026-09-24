using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Inventory.Posting;

/// <summary>在调用方事务中锁定库存事实、维护余额并追加不可变流水。</summary>
public interface IInventoryPostingRepository
{
    /// <summary>锁定唯一余额投影。</summary>
    Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid productId);
    /// <summary>按幂等明细键读取流水。</summary>
    Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId);
    /// <summary>按幂等明细键锁定流水。</summary>
    Task<InventoryLedgerEntry?> LockLedgerByCommandItemAsync(Guid commandItemId);
    /// <summary>按主键锁定原流水。</summary>
    Task<InventoryLedgerEntry?> LockLedgerByIdAsync(Guid ledgerId);
    /// <summary>锁定原流水对应的反向流水。</summary>
    Task<InventoryLedgerEntry?> LockReversalByOriginalAsync(Guid originalLedgerId);
    /// <summary>锁定原流水对应的替代流水。</summary>
    Task<InventoryLedgerEntry?> LockReplacementByOriginalAsync(Guid originalLedgerId);
    /// <summary>锁定纠错组全部事实。</summary>
    Task<IReadOnlyList<InventoryLedgerEntry>> LockCorrectionGroupAsync(Guid correctionGroupId);
    /// <summary>读取库存身份存在且未软删除的产品。</summary>
    Task<Product?> GetProductForPostingAsync(Guid productId);
    /// <summary>取得事务级库存命令应用锁；保留明细幂等保护。</summary>
    Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default);
    /// <summary>创建余额投影。</summary>
    Task<int> InsertBalanceAsync(InventoryBalance balance);
    /// <summary>按并发版本更新余额投影。</summary>
    Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion);
    /// <summary>取得下一流水序号。</summary>
    Task<long> NextLedgerSequenceAsync();
    /// <summary>追加库存流水。</summary>
    Task<int> InsertLedgerAsync(InventoryLedgerEntry entry);
}
