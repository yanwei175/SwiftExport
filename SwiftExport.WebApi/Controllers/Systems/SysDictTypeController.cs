using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>
/// 系统字典类型控制器
/// 支持标准 CRUD 操作和分组树查询
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SysDictTypeController(ISysDictTypeService service)
        : ControllerBase
{
    /// <summary>
    /// 获取所有字典类型（平面列表）
    /// </summary>
    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllTypes()
    {
        return await service.GetAllAsync();
    }

    /// <summary>
    /// 获取字典类型分组树
    /// 按 GroupCode 分组，返回一层分组树结构
    /// 
    /// 返回示例：
    /// [
    ///   {
    ///     "nodeId": "Basic",
    ///     "nodeName": "基础数据",
    ///     "nodeType": "Group",
    ///     "children": [
    ///       {
    ///         "nodeId": "1",
    ///         "nodeName": "性别 (Sex)",
    ///         "nodeType": "DictType",
    ///         "dictTypeData": { ... }
    ///       }
    ///     ]
    ///   }
    /// ]
    /// </summary>
    [HttpGet("group-tree")]
    public async Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTree()
    {
        return await service.GetDictTypeGroupTreeAsync();
    }
    [HttpGet("{id}")]
    public async Task<ServiceResult<SysDictTypeDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] SysDictTypeDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] SysDictTypeDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<SysDictTypeDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<BatchResultDto>> BatchSave([FromBody] BatchRequestDto<SysDictTypeDto> req) => await service.BatchSaveAsync(req);

}
