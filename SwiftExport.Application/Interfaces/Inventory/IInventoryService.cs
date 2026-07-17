using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

public interface IInventoryService
{
    Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchWarehouseSitesAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<WarehouseSiteDto>> GetWarehouseSiteAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualWarehouseAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<WarehouseSiteDto>> UpdateWarehouseSiteAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<WarehouseSiteDto>> DeactivateWarehouseSiteAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<InventoryPostingResultDto>>> PreviewOpeningBalancesAsync(OpeningBalanceCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<InventoryPostingResultDto>>> PostOpeningBalancesAsync(OpeningBalanceCommand command, CancellationToken cancellationToken = default);
}
