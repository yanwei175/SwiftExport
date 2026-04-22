using Dapper;
using SwiftExport.Core.Entities.SystemEntities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories.SystemRepositories
{
    /// <summary>
    /// 视图路由配置仓储实现
    /// 继承 DapperRepository，获得通用增删改查能力；
    /// 额外实现 GetEnabledRoutesAsync，供前端启动缓存使用。
    /// </summary>
    public class SysViewRouteRepository(RepositoryBaseContext context)
        : DapperRepository<SysViewRoute>(context), ISysViewRouteRepository
    {
        /// <summary>
        /// 获取所有启用且未删除的路由配置，按 SortOrder 升序排列。
        /// </summary>
        public async Task<IReadOnlyList<SysViewRoute>> GetEnabledRoutesAsync()
        {
            const string sql = @"
                SELECT * FROM SysViewRoute
                WHERE IsEnabled = 1
                  AND (IsDeleted = 0 OR IsDeleted IS NULL)
                ORDER BY SortOrder ASC";

            var result = await Connection.QueryAsync<SysViewRoute>(sql, transaction: Transaction);
            return result.AsList();
        }
    }
}
