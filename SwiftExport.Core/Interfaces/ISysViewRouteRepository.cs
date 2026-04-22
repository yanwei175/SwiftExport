using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.SystemEntities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
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
    }
}
