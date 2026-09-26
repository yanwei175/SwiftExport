using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Inventory;

/// <summary>定义库存余额投影、不可变流水查询及事务锁的数据访问边界。</summary>
public interface IInventoryRepository
{
    /// <summary>分页查询库存余额。</summary>
    Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>分页查询库存流水。</summary>
    Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取余额详情。</summary>
    Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>分页读取余额键对应流水。</summary>
    Task<PagedResponseBase<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>读取余额与流水净额的对账结果。</summary>
    Task<InventoryReconciliationDto?> GetReconciliationAsync(Guid balanceId, CancellationToken cancellationToken = default);
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
    /// <summary>读取库存身份仍存在且未软删除的产品；是否允许具体业务履约由调用方决定。</summary>
    Task<Product?> GetProductForPostingAsync(Guid productId);
    /// <summary>取得事务级库存命令应用锁。</summary>
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
