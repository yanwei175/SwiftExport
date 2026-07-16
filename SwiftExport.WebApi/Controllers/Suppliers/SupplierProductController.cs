using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Suppliers;

public sealed class SupplierProductController(ISupplierProductService service)
    : BaseController<SupplierProduct, SupplierProductDto>(service)
{
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPaged(
        [FromBody] SupplierProductSearchRequest request) => service.GetPagedAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSave(
        [FromBody] SupplierProductBatchSaveRequest request) => service.BatchSaveAsync(request);

    [HttpPost("set-product-default-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplier(
        [FromBody] SetProductDefaultSupplierCommand command) => service.SetProductDefaultSupplierAsync(command);
}
