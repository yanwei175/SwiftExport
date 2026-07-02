using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SystemEntities;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 视图路由配置服务接口
    /// 继承通用 CRUD，扩展"获取所有启用路由"方法。
    /// </summary>
    public partial interface ISysViewRouteService
    {
        /// <summary>
        /// 获取所有启用的路由配置（IsEnabled=true, IsDeleted=false）。
        /// </summary>
        Task<ServiceResult<IReadOnlyList<SysViewRouteDto>>> GetEnabledRoutesAsync();
    }
}
