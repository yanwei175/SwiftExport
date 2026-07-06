using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

using SwiftExport.Contracts.DTOs.PackingLists.Requests;
using SwiftExport.Contracts.DTOs.PackingLists.Responses;
namespace SwiftExport.WebApi.Controllers.Orders;

[ApiController]
[Route("api/[controller]")]
public class PackingListController(IPackingListService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<PackingListHeadDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] PackingListHeadDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] PackingListHeadDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<PackingListBatchSaveResponse>> BatchSave([FromBody] PackingListBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetPagedListAsync([FromBody] PackingListSearchDto search)
    {
        return await service.GetHeadPagedListAsync(search);
    }

    [HttpGet("{id}/detail")]
    public async Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(Guid id)
    {
        return await service.GetDetailByIdAsync(id);
    }

    [HttpPost("detail/save")]
    public async Task<ServiceResult<Guid>> SaveDetailAsync([FromBody] PackingListDetailDto request)
    {
        return await service.SaveDetailAsync(request);
    }

    [HttpPost("post")]
    public async Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync([FromBody] PackingListPostRequestDto request)
    {
        return await service.PostPackingListAsync(request);
    }
}
