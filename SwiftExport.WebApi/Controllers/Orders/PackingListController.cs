using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Orders;

public class PackingListController(IPackingListService service)
    : BaseController<PackingListHead, PackingListHeadDto>(service)
{
    [HttpPost("search")]
    public async Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetPagedListAsync([FromBody] PackingListSearchDto search)
    {
        return await service.GetHeadPagedListAsync(search);
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
