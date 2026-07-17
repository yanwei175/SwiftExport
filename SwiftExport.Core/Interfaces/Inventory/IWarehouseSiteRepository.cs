using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Core.Interfaces.Inventory;

public interface IWarehouseSiteRepository
{
    Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);
    Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<WarehouseSite?> GetForUpdateAsync(Guid uid);
    Task<bool> IsActiveSupplierAsync(Guid supplierId);
    Task<int> InsertAsync(WarehouseSite site);
    Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion);
    Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion);
    Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId);
}
