using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.PackingLists.Requests;
using SwiftExport.Contracts.DTOs.PackingLists.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Orders;


public class PackingListController(IPackingListService service) : BaseController<PackingListHead, PackingListHeadDto>(service)
{


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<PackingListBatchSaveResponse>> BatchSave([FromBody] PackingListBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<PackingListHeadDto>>> GetPagedAsync([FromBody] PackingListSearchRequest request)
        => await service.GetHeadPagedListAsync(request);

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
