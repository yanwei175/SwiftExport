using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;
using System.Collections.Generic;
using System.Threading.Tasks;

using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
namespace SwiftExport.WebApi.Controllers.Products;

[ApiController]
[Route("api/[controller]")]
public class BomController(IBomService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<BomDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] BomDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] BomDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<BomDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

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
