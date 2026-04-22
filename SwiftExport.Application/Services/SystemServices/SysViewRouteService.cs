using MapsterMapper;
using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SystemEntities;
using SwiftExport.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Application.Services.SystemServices
{
    /// <summary>
    /// 视图路由配置服务实现
    /// 继承 BaseService，获得通用 CRUD；
    /// 额外实现 GetEnabledRoutesAsync，供前端拉取缓存。
    /// </summary>
    public class SysViewRouteService(
        ISysViewRouteRepository repository,
        ServiceBaseContext context)
        : BaseService<SysViewRoute, SysViewRouteDto>(repository, context), ISysViewRouteService
    {
        // 保持对具体仓储的引用，以访问扩展方法
        private readonly ISysViewRouteRepository _routeRepo = repository;

        /// <summary>
        /// 获取所有启用的路由配置，映射为 DTO 列表返回。
        /// </summary>
        public async Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutesAsync()
        {
            var entities = await _routeRepo.GetEnabledRoutesAsync();
            var dtos = entities.Adapt<IReadOnlyList<SysViewRouteDto>>();
            return ServiceResult<IReadOnlyList<SysViewRouteDto>>.Ok(dtos);
        }
    }
}
