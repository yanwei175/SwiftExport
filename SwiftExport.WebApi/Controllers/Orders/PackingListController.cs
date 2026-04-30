using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders;

public class PackingListController(IPackingListService service)
    : BaseController<PackingListHead, PackingListDto>(service)
{
    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<PackingListDto>>> GetPagedListAsync([FromBody] PackingListSearchDto search)
    {
        return await service.GetPagedListAsync(search);
    }

    [HttpGet("{id}/detail")]
    public async Task<ServiceResult<PackingListDetailDto>> GetDetailByIdAsync(int id)
    {
        return await service.GetDetailByIdAsync(id);
    }

    [HttpPost("detail/save")]
    public async Task<ServiceResult<int>> SaveDetailAsync([FromBody] PackingListDetailDto request)
    {
        return await service.SaveDetailAsync(request);
    }

    [HttpPost("post")]
    public async Task<ServiceResult<PackingListPostResultDto>> PostPackingListAsync([FromBody] PackingListPostRequestDto request)
    {
        return await service.PostPackingListAsync(request);
    }
}
