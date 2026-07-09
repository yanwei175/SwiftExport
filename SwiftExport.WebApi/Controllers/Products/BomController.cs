using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Products;

public class BomController(IBomService service) : BaseController<Bom, BomDto>(service)
{


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<BomBatchSaveResponse>> BatchSave([FromBody] BomBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpGet("tree/{productSku}")]
    public async Task<ServiceResult<IEnumerable<BomTreeDto>>> GetBomTree(string productSku)
    {
        return await service.GetBomTreeAsync(productSku);
    }

    [HttpGet("by-no/{bomNo}")]
    public async Task<ServiceResult<List<BomDto>>> GetBomDtoByBomNo(string bomNo)
    {
        return await service.GetBomDtoByBomNoAsync(bomNo);
    }


    [HttpGet("validate/{productSku}")]
    public async Task<ServiceResult<bool>> ValidateBom(string productSku)
    {
        return await service.ValidateBomAsync(productSku);
    }
}
