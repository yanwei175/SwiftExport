using SwiftExport.Core.Entities.Systems.ViewRoutes;
using SwiftExport.Core.Common;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces.Systems.ViewRoutes
{
    /// <summary>
    /// 视图路由配置仓储接口
    /// </summary>
    public interface ISysViewRouteRepository : IRepository<SysViewRoute>
    {
        /// <summary>
        /// 获取所有启用的路由配置（IsEnabled=true, IsDeleted=false）
        /// 用于前端启动时加载路由缓存。
        /// </summary>
        Task<IReadOnlyList<SysViewRoute>> GetEnabledRoutesAsync();

        Task<PagedResponseBase<SysViewRouteDto>> GetPagedAsync(SysViewRouteSearchRequest request);

        Task<IReadOnlyList<SysViewRoute>> GetAllForUpdateAsync();

        Task<IReadOnlyList<SysViewRoute>> GetByTargetViewKeysAsync(IEnumerable<string> targetViewKeys, bool withUpdLock = false);
    }
}
