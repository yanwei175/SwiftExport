using Azure.Core;
using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

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
        public async Task<IReadOnlyList<DependentDemand>> GetListBySourceTypeAndSourceUidAsync(string sourceType, Guid sourceUid)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || sourceUid == Guid.Empty) return [];

            var list = await GetListAsync(new { SourceType = sourceType.Trim(), SourceUid = sourceUid });
            return [.. list];
        }

        /// <summary>
        /// 根据来源类型与来源销售单行 Id 查询（可能多行）。
        /// </summary>
        public async Task<IReadOnlyList<DependentDemand>> GetListBySourceSoLineIdAsync(string sourceType, Guid sourceSoLineId)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || sourceSoLineId == Guid.Empty)
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

        public async Task<IReadOnlyList<DependentDemand>> GetListByTargetLineIdsAsync(IEnumerable<Guid> targetIdList, bool withUpdLock = false)
        {
            var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
            var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [TargetLineId] IN @TargetIdList AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            var rst= await Connection.QueryAsync<DependentDemand>(sql, new { TargetIdList = targetIdList }, transaction: Transaction);
            return [.. rst];
        }

        public Task<int> ResetByTargetLineIdsAsync(IEnumerable<Guid> targetIdList)
        {
            var sql = $"UPDATE [{TableName}] SET TargetLineId = NULL, TargetNo = NULL, StatusFlag = 1 " +
                      $"WHERE TargetLineId IN @TargetIdList";
            return Connection.ExecuteAsync(sql, new { TargetIdList = targetIdList }, transaction: Transaction);
        }
    }
}
