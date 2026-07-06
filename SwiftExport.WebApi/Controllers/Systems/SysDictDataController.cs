using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
namespace SwiftExport.WebApi.Controllers.Systems;

[ApiController]
[Route("api/[controller]")]
public class SysDictDataController(ISysDictDataService service) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ServiceResult<SysDictDataDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] SysDictDataDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] SysDictDataDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<SysDictDataDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

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
