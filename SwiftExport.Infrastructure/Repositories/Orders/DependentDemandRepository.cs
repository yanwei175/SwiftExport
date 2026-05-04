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
    public partial class DependentDemandRepository(RepositoryBaseContext context)
        : DapperRepository<DependentDemand>(context), IDependentDemandRepository
    {
        /// <summary>
        /// 根据来源类型与来源销售单号查询。
        /// </summary>
        public async Task<IReadOnlyList<DependentDemand>> GetBySourceSoNoAsync(string sourceType, string sourceSoNo)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceSoNo)) return [];

            var list = await GetListAsync(new { SourceType = sourceType.Trim(), SourceNo = sourceSoNo.Trim() });
            return [.. list];
        }

        /// <summary>
        /// 根据来源类型与来源销售单行 Id 查询（可能多行）。
        /// </summary>
        public async Task<IReadOnlyList<DependentDemand>> GetListBySourceSoLineIdAsync(string sourceType, long sourceSoLineId)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || sourceSoLineId <= 0)
                return [];

            var list = await GetListAsync(new { SourceType = sourceType.Trim(), SourceLineId = sourceSoLineId });
            return [.. list];
        }

        /// <summary>
        /// 根据来源类型与来源单号查询需求池记录。
        /// </summary>
        public async Task<IReadOnlyList<DependentDemand>> GetBySourceAsync(string sourceType, string sourceNo)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo))
                return [];

            var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE SourceType = @SourceType AND SourceNo = @SourceNo AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            var list = await Connection.QueryAsync<DependentDemand>(sql, new { SourceType = sourceType.Trim(), SourceNo = sourceNo.Trim() }, transaction: Transaction);
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
