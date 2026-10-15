using SwiftExport.Application.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Application.Interfaces.Systems.Dictionaries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Systems.Dictionaries.Types;

/// <summary>
/// 系统字典类型控制器
/// 支持标准 CRUD、分组树查询及显式同步内置字典。
/// </summary>
public class SysDictTypeController(ISysDictTypeService service, IBuiltInDictionarySyncService builtInSync) : BaseController<SysDictType, SysDictTypeDto>(service)
{
    /// <summary>由字典编辑权限持有者明确触发内置类型与默认值落库，转交同步用例控制事务。</summary>
    [Authorize]
    [HttpPost("sync-built-in")]
    public Task<ServiceResult<BuiltInDictionarySyncResponse>> SyncBuiltInAsync(CancellationToken cancellationToken) =>
        builtInSync.SyncAsync(cancellationToken);

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

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>> GetPaged([FromBody] SysDictTypeSearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SysDictTypeBatchSaveResponse>> BatchSave([FromBody] SysDictTypeBatchSaveRequest req) => await service.BatchSaveAsync(req);

}
