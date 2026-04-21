using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Systems;

public class SysDictDataController(ISysDictDataService service)
        : BaseController<SysDictData, SysDictDataDto>(service)
{
    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAll()
    {
        return await _service.GetAllAsync();
    }

    // 基类的增删改查全有了，只需要把“按字典Code查询”这个特有业务写上就行了

    [HttpGet("by-code/{dictCode}")]
    public async Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCode(string dictCode)
    {
        // 利用你基建里的匿名对象智能查询
        return await _service.GetListAsync(new { DictCode = dictCode, IsDeleted = 0 });
    }
}
