using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>
/// 系统字典类型控制器
/// 支持标准 CRUD 操作和分组树查询
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SysDictTypeController(ISysDictTypeService service)
        : BaseController<SysDictType, SysDictTypeDto>(service)
{
    private readonly ISysDictTypeService _dictTypeService = service;

    /// <summary>
    /// 获取所有字典类型（平面列表）
    /// </summary>
    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllTypes()
    {
        // 直接调用基类 _service 里已经写好的 GetAllAsync 方法
        return await _service.GetAllAsync();
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
        return await _dictTypeService.GetDictTypeGroupTreeAsync();
    }
}

