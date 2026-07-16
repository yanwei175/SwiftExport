using Dapper;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public partial class IndependentDemandRepository(RepositoryBaseContext context) : DapperRepository<IndependentDemand>(context), IIndependentDemandRepository
{
    public async Task<bool> DeleteRangeBySourceTypeAndSourceIdListAsync(string sourceType, IEnumerable<Guid> sourceIds)
    {
        var sql = $"DELETE FROM [IndependentDemand] WHERE SourceType =@sourceType AND SourceLineId IN @sourceIds";
        var rst = await Connection.ExecuteAsync(sql, new { sourceType, sourceIds }, transaction: Transaction);
        return rst > 0;
    }

    public async Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceUidAsync(string sourceType, Guid uid, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $"SELECT * FROM [IndependentDemand] {lockHint} WHERE SourceType = @sourceType AND SourceUid = @uid AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<IndependentDemand>(sql, new { sourceType, uid }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<IReadOnlyList<RepositoryWriteResult>> AddWorkspaceDemandsAsync(IEnumerable<IndependentDemand> demands)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var demand in demands) results.Add(await AddAsync(demand));
        return results;
    }

    public async Task<bool> HasSalesOrderCancellationBlockerAsync(string sourceType, Guid sourceUid, IEnumerable<Guid> sourceLineIds)
    {
        var lineIds = sourceLineIds.Distinct().ToArray();
        if (lineIds.Length == 0) return false;

        const string sql = @"
            SELECT CASE WHEN
                EXISTS (
                    SELECT 1 FROM IndependentDemand d WITH(UPDLOCK, ROWLOCK)
                    WHERE d.SourceType = @SourceType AND d.SourceUid = @SourceUid AND d.IsDeleted = 0
                      AND (d.IsDecomposed = 1 OR d.FilledQty > 0 OR d.ShippedQty > 0 OR d.IsShipped = 1
                           OR d.OrderStatus <> @ConfirmedDemandStatus))
                OR EXISTS (
                    SELECT 1
                    FROM DependentDemand dd WITH(UPDLOCK, ROWLOCK)
                    INNER JOIN IndependentDemand d ON d.Uid = dd.IndependentDemandId
                    WHERE d.SourceType = @SourceType AND d.SourceUid = @SourceUid
                      AND d.IsDeleted = 0 AND dd.IsDeleted = 0)
                OR EXISTS (
                    SELECT 1 FROM PackingListLine pl WITH(NOLOCK)
                    WHERE pl.SoLineId IN @LineIds AND (pl.IsDeleted = 0 OR pl.IsDeleted IS NULL))
                OR EXISTS (
                    SELECT 1 FROM PurchaseReceiveLine rl WITH(NOLOCK)
                    WHERE rl.SoLineId IN @LineIds AND (rl.IsDeleted = 0 OR rl.IsDeleted IS NULL))
            THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        return await Connection.ExecuteScalarAsync<bool>(sql, new
        {
            SourceType = sourceType,
            SourceUid = sourceUid,
            LineIds = lineIds,
            ConfirmedDemandStatus = IndependentDemandStatusEnum.已确认
        }, transaction: Transaction);
    }

    public Task<int> SoftDeleteBySourceAsync(string sourceType, Guid sourceUid)
        => Connection.ExecuteAsync(@"
            UPDATE IndependentDemand
            SET IsDeleted = 1,
                UpdatedDate = @Now,
                UpdatedBy = @UserName,
                Version = Version + 1
            WHERE SourceType = @SourceType AND SourceUid = @SourceUid AND IsDeleted = 0;",
            new
            {
                SourceType = sourceType,
                SourceUid = sourceUid,
                Now = DateTime.Now,
                UserName = CurrentUser.UserName
            }, transaction: Transaction);

    public async Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceNoAsync(string sourceType, string sourceNo)
    {
        var sql = $"SELECT * FROM [IndependentDemand] WHERE SourceType = @sourceType AND SourceNo = @sourceNo AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<IndependentDemand>(sql, new { sourceType, sourceNo }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceLineIdsAsync(string sourceType, IEnumerable<Guid> sourceLineIds)
    {
        var ids = (sourceLineIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var sql = $"SELECT * FROM [IndependentDemand] WHERE SourceType = @sourceType AND SourceLineId IN @sourceLineIds AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<IndependentDemand>(sql, new { sourceType, sourceLineIds = ids }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<int> TryMarkDecomposedAsync(string sourceType, string sourceNo, IEnumerable<Guid> sourceLineIds, DateTime decomposedTime)
    {
        var ids = (sourceLineIds ?? []).Distinct().ToList();
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo) || ids.Count == 0)
            return 0;

        var sql = @"UPDATE [IndependentDemand]
                    SET IsDecomposed = 1,
                        DecomposedTime = @decomposedTime,
                        UpdatedDate = @decomposedTime,
                        ChangeTime = @decomposedTime
                    WHERE SourceType = @sourceType
                      AND SourceNo = @sourceNo
                      AND SourceLineId IN @sourceLineIds
                      AND (IsDecomposed = 0 OR IsDecomposed IS NULL)
                      AND IsDeleted = 0";

        return await Connection.ExecuteAsync(sql, new
        {
            sourceType,
            sourceNo,
            sourceLineIds = ids,
            decomposedTime
        }, transaction: Transaction);
    }

    public async Task<int> RollbackDecomposedMarkAsync(string sourceType, string sourceNo, IEnumerable<Guid> sourceLineIds)
    {
        var ids = (sourceLineIds ?? []).Distinct().ToList();
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceNo) || ids.Count == 0)
            return 0;

        var now = DateTime.Now;
        var sql = @"UPDATE [IndependentDemand]
                    SET IsDecomposed = 0,
                        DecomposedTime = NULL,
                        UpdatedDate = @now,
                        ChangeTime = @now
                    WHERE SourceType = @sourceType
                      AND SourceNo = @sourceNo
                      AND SourceLineId IN @sourceLineIds
                      AND IsDeleted = 0";

        return await Connection.ExecuteAsync(sql, new
        {
            sourceType,
            sourceNo,
            sourceLineIds = ids,
            now
        }, transaction: Transaction);
    }

    public async Task<bool> CanCanceAsync(IEnumerable<Guid> independIds)
    {
        var ids = independIds.Distinct().ToArray();
        if (ids.Length == 0) return true;
        var sql = @"SELECT COUNT(1)
                    FROM IndependentDemand d
                    WHERE d.Uid IN @Ids AND d.IsDeleted = 0
                      AND (d.IsDecomposed = 1 OR d.FilledQty > 0 OR d.ShippedQty > 0
                           OR EXISTS (SELECT 1 FROM DependentDemand dd WHERE dd.IndependentDemandId = d.Uid AND dd.IsDeleted = 0));";
        var rst = await Connection.ExecuteScalarAsync<int>(sql, new { Ids = ids }, transaction: Transaction);
        return rst == 0;
    }
}
