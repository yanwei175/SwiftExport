using SwiftExport.Core.Entities.Systems.ViewRoutes;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.Systems.ViewRoutes;

/// <summary>
/// 视图路由配置服务接口
/// 继承通用 CRUD，扩展"获取所有启用路由"方法。
/// </summary>
public partial interface ISysViewRouteService : IBaseService<SysViewRoute, SysViewRouteDto>
{

    Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SysViewRouteDto>>> GetPagedAsync(SysViewRouteSearchRequest request);
    Task<ServiceResult<SysViewRouteBatchSaveResponse>> BatchSaveAsync(SysViewRouteBatchSaveRequest batchReq);

    /// <summary>
    /// 获取所有启用的路由配置（IsEnabled=true, IsDeleted=false）。
    /// </summary>
    Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutesAsync();
}
