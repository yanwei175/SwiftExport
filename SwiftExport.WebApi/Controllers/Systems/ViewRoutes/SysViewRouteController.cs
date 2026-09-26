using SwiftExport.Application.Interfaces.Systems.ViewRoutes;
using SwiftExport.Core.Entities.Systems.ViewRoutes;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Systems.ViewRoutes;

/// <summary>
/// 视图路由配置控制器
/// 此处额外暴露 /enabled 接口，供前端启动时批量拉取路由缓存。
/// </summary>
public class SysViewRouteController(ISysViewRouteService service)
    : BaseController<SysViewRoute, SysViewRouteDto>(service)
{


    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SysViewRouteDto>>> GetPaged([FromBody] SysViewRouteSearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SysViewRouteBatchSaveResponse>> BatchSave([FromBody] SysViewRouteBatchSaveRequest req) => await service.BatchSaveAsync(req);

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }

    /// <summary>
    /// 获取所有启用的路由配置（供前端缓存使用）。
    /// GET /api/SysViewRoute/enabled
    /// </summary>
    [HttpGet("enabled")]
    public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutes()
    {
        return await service.GetEnabledRoutesAsync();
    }
}
