using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Suppliers;

public class SupplierController(ISupplierService service) : BaseController<Supplier, SupplierDto>(service)
{

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedList([FromBody] SupplierSearchRequest request)
        => await service.GetPagedAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SupplierBatchSaveResponse>> BatchSave([FromBody] SupplierBatchSaveRequest req) => await service.BatchSaveAsync(req);


    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }


}
