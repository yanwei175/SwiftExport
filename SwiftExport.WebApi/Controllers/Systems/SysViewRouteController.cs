using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SystemEntities;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Systems
{
    /// <summary>
    /// 视图路由配置控制器
    /// 基类提供标准 CRUD + BatchSave + GetPagedList；
    /// 此处额外暴露 /enabled 接口，供前端启动时批量拉取路由缓存。
    /// </summary>
    public class SysViewRouteController(ISysViewRouteService service)
        : BaseController<SysViewRoute, SysViewRouteDto>(service)
    {
        // 保持对具体 Service 的引用，以访问扩展方法
        private readonly ISysViewRouteService _routeService = service;
        [HttpGet("all")]
        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAll()
        {
            return await _service.GetAllAsync();
        }

        /// <summary>
        /// 获取所有启用的路由配置（供前端缓存使用）。
        /// GET /api/SysViewRoute/enabled
        /// </summary>
        [HttpGet("enabled")]
        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutes()
        {
            return await _routeService.GetEnabledRoutesAsync();
        }
    }
}
