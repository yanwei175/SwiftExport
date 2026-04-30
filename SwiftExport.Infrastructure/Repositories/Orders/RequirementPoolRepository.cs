using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using Dapper;

namespace SwiftExport.Infrastructure.Repositories.Orders
{
    /// <summary>
    /// 销售订单拆解中间表仓储实现。
    /// </summary>
    public partial class RequirementPoolRepository(RepositoryBaseContext context)
        : DapperRepository<RequirementPool>(context), IRequirementPoolRepository
    {
        /// <summary>
        /// 根据来源销售单号查询。
        /// </summary>
        public async Task<IReadOnlyList<RequirementPool>> GetBySourceSoNoAsync(string sourceSoNo)
        {
            if (string.IsNullOrWhiteSpace(sourceSoNo)) return [];

            var list = await GetListAsync(new { SourceSoNo = sourceSoNo.Trim() });
            return [.. list];
        }

        /// <summary>
        /// 根据来源销售单行 Id 查询。
        /// </summary>
        public async Task<RequirementPool?> GetBySourceSoLineIdAsync(long sourceSoLineId)
        {
            var list = await GetListAsync(new { SourceSoLineId = sourceSoLineId });
            return list.FirstOrDefault();
        }

        /// <summary>
        /// 根据来源类型与来源单号查询需求池记录。
        /// </summary>
        public async Task<IReadOnlyList<RequirementPool>> GetBySourceAsync(string sourceType, string sourceNo)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
                return [];

            var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE SourceType = @SourceType AND SourceNo = @SourceNo AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            var list = await Connection.QueryAsync<RequirementPool>(sql, new { SourceType = sourceType.Trim(), SourceNo = sourceNo.Trim() }, transaction: Transaction);
            return [.. list];
        }

        /// <summary>
        /// 删除来源类型与来源单号对应的需求池记录。
        /// </summary>
        public async Task<int> DeleteBySourceAsync(string sourceType, string sourceNo)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
                return 0;

            var sql = $"DELETE FROM [{TableName}] WHERE SourceType = @SourceType AND SourceNo = @SourceNo";
            return await Connection.ExecuteAsync(sql, new { SourceType = sourceType.Trim(), SourceNo = sourceNo.Trim() }, transaction: Transaction);
        }
    }
}
