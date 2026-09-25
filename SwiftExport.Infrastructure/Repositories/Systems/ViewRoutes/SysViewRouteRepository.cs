using SwiftExport.Core.Entities.Systems.ViewRoutes;
using SwiftExport.Core.Interfaces.Systems.ViewRoutes;
using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories.Systems.ViewRoutes
{
    /// <summary>
    /// 视图路由配置仓储实现
    /// 继承 DapperRepository，获得通用增删改查能力；
    /// 额外实现 GetEnabledRoutesAsync，供前端启动缓存使用。
    /// </summary>
    public class SysViewRouteRepository(RepositoryBaseContext context)
        : DapperRepository<SysViewRoute>(context), ISysViewRouteRepository
    {
        private const string Columns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, TargetViewKey, ViewModelTypeName, ViewTypeName, DisplayName, IsEnabled, SortOrder, RouteCategory, AssemblyName, Remark";

        /// <summary>
        /// 获取所有启用且未删除的路由配置，按 SortOrder 升序排列。
        /// </summary>
        public async Task<IReadOnlyList<SysViewRoute>> GetEnabledRoutesAsync()
        {
            var sql = $@"
                SELECT {Columns} FROM dbo.SysViewRoute
                WHERE IsEnabled = 1
                  AND (IsDeleted = 0 OR IsDeleted IS NULL)
                ORDER BY SortOrder, TargetViewKey, Uid";

            var result = await Connection.QueryAsync<SysViewRoute>(sql, transaction: Transaction);
            return result.AsList();
        }

        public async Task<PagedResponseBase<SysViewRouteDto>> GetPagedAsync(SysViewRouteSearchRequest request)
        {
            request ??= new SysViewRouteSearchRequest();
            var pageIndex = Math.Max(1, request.PageIndex);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
            var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
            var filters = new List<string> { "(IsDeleted = 0 OR IsDeleted IS NULL)" };

            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                filters.Add("(TargetViewKey LIKE @Keyword OR DisplayName LIKE @Keyword OR ViewModelTypeName LIKE @Keyword OR RouteCategory LIKE @Keyword)");
                parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
            }

            var batchKeywords = request.BatchKeywords?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            if (batchKeywords.Length > 0)
            {
                filters.Add("TargetViewKey IN @BatchKeywords");
                parameters.Add("BatchKeywords", batchKeywords);
            }

            var where = string.Join(" AND ", filters);
            var total = await Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM dbo.SysViewRoute WHERE {where}", parameters, Transaction);
            var items = await Connection.QueryAsync<SysViewRouteDto>($@"
                SELECT {Columns}
                FROM dbo.SysViewRoute
                WHERE {where}
                ORDER BY SortOrder, TargetViewKey, Uid
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", parameters, Transaction);

            return new PagedResponseBase<SysViewRouteDto>
            {
                Items = items.AsList(),
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        public async Task<IReadOnlyList<SysViewRoute>> GetAllForUpdateAsync()
        {
            var rows = await Connection.QueryAsync<SysViewRoute>(
                $"SELECT {Columns} FROM dbo.SysViewRoute WITH (UPDLOCK, ROWLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL",
                transaction: Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<SysViewRoute>> GetByTargetViewKeysAsync(
            IEnumerable<string> targetViewKeys,
            bool withUpdLock = false)
        {
            var keys = targetViewKeys.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (keys.Length == 0) return [];
            var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
            var rows = await Connection.QueryAsync<SysViewRoute>(
                $"SELECT {Columns} FROM dbo.SysViewRoute {lockHint} WHERE TargetViewKey IN @Keys AND (IsDeleted = 0 OR IsDeleted IS NULL)",
                new { Keys = keys }, Transaction);
            return rows.AsList();
        }
    }
}
