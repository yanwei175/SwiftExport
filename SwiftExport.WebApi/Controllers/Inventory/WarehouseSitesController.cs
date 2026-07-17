using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

[ApiController]
[Route("api/inventory/warehouse-sites")]
public sealed class WarehouseSitesController(IInventoryService service) : ControllerBase
{
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync([FromBody] WarehouseSiteSearchRequest request, CancellationToken cancellationToken) => service.SearchWarehouseSitesAsync(request, cancellationToken);
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken) => service.GetWarehouseSiteAsync(uid, cancellationToken);
    [HttpPost("supplier-virtual")]
    public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync([FromBody] CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken) => service.CreateSupplierVirtualWarehouseAsync(command, cancellationToken);
    [HttpPut("{uid:guid}")]
    public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, [FromBody] UpdateWarehouseSiteCommand command, CancellationToken cancellationToken) => service.UpdateWarehouseSiteAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/deactivate")]
    public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, [FromBody] DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken) => service.DeactivateWarehouseSiteAsync(uid, command, cancellationToken);
}
