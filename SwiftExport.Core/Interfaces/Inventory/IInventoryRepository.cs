using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Inventory;

public interface IInventoryRepository
{
    Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);
    Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, CancellationToken cancellationToken = default);
    Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid productId);
    Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId);
    Task<InventoryLedgerEntry?> LockLedgerByCommandItemAsync(Guid commandItemId);
    Task<Product?> GetActiveProductAsync(Guid productId);
    Task<int> InsertBalanceAsync(InventoryBalance balance);
    Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion);
    Task<long> NextLedgerSequenceAsync();
    Task<int> InsertLedgerAsync(InventoryLedgerEntry entry);
}
