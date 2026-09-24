using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Inventory.Query;

/// <summary>读取库存余额、历史流水及余额对账投影，不写库存事实。</summary>
public interface IInventoryQueryRepository
{
    /// <summary>按筛选条件分页读取库存余额。</summary>
    Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>按筛选条件分页读取库存流水。</summary>
    Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取指定余额及其显示资料。</summary>
    Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>分页读取指定余额键的历史流水。</summary>
    Task<PagedResponseBase<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>读取余额投影与不可变流水的对账结果。</summary>
    Task<InventoryReconciliationDto?> GetReconciliationAsync(Guid balanceId, CancellationToken cancellationToken = default);
}
