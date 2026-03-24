using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;


namespace SwiftExport.WebApi.Controllers.Systems;

public class SysDictTypeController(ISysDictTypeService service)
        : BaseController<SysDictType, DictTypeDto>(service)
{
    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<DictTypeDto>>> GetAllTypes()
    {
        // 直接调用基类 _service 里已经写好的 GetAllAsync 方法
        return await _service.GetAllAsync();
    }
}
