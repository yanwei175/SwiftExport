using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Systems;


public class SysDictDataController(ISysDictDataService service) : BaseController<SysDictData, SysDictDataDto>(service)
{


    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SysDictDataDto>>> GetPaged([FromBody] SysDictDataSearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SysDictDataBatchSaveResponse>> BatchSave([FromBody] SysDictDataBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }

    // 基类的增删改查全有了，只需要把“按字典Code查询”这个特有业务写上就行了

    [HttpGet("by-code/{dictCode}")]
    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCode(string dictCode)
    {
        return await service.GetByCodeAsync(dictCode);
    }
}
