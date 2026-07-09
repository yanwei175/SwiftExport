using Dapper;
using SwiftExport.Contracts.SysEnums;
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

    public async Task<IReadOnlyList<IndependentDemand>> GetListBySourceTypeAndSourceUidAsync(string sourceType, Guid uid)
    {
        var sql = $"SELECT * FROM [IndependentDemand] WHERE SourceType =@sourceType AND SourceUid = @uid AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<IndependentDemand>(sql, new { sourceType, uid }, transaction: Transaction);
        return [.. rst];
    }

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

    public async Task<int> 批量更新完工数量Async(IDictionary<string, IDictionary<Guid, decimal>> sourceTypeLineQtyMap)
    {
        throw new NotImplementedException();
        //if (sourceTypeLineQtyMap == null || sourceTypeLineQtyMap.Count == 0)
        //    return 0;

        //var totalAffected = 0;
        //foreach (var kv in sourceTypeLineQtyMap)
        //{
        //    var sourceType = kv.Key;
        //    var sourceLineIdQtyMap = kv.Value;

        //    if (string.IsNullOrWhiteSpace(sourceType) || sourceLineIdQtyMap == null || sourceLineIdQtyMap.Count == 0)
        //        continue;

        //    var sourceLineIds = sourceLineIdQtyMap.Keys.Distinct().ToList();
        //    var demands = await GetListBySourceTypeAndSourceLineIdsAsync(sourceType, sourceLineIds);
        //    if (demands.Count == 0)
        //        continue;

        //    foreach (var demand in demands)
        //    {
        //        if (!sourceLineIdQtyMap.TryGetValue(demand.SourceLineId, out var addQty) || addQty <= 0)
        //            continue;

        //        demand.FilledQty += addQty;

        //        if (demand.FilledQty >= demand.OriginalQty && demand.OrderStatus != IndependentDemandStatusEnum.已完成)
        //            demand.OrderStatus = IndependentDemandStatusEnum.已到货;

        //        if (demand.FilledQty >= demand.OriginalQty && demand.ShippedQty >= demand.OriginalQty)
        //            demand.OrderStatus = IndependentDemandStatusEnum.已完成;
        //    }

        //    totalAffected += await UpdateRangeAsync(demands);
        //}

        //return totalAffected;
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
        //判断净需求下对应毛需求的状态是否为草稿，如果毛需求不是草稿，则不允许取消净需求
        var sql = @"SELECT COUNT(1) FROM [IndependentDemand]
                    WHERE Uid IN @independIds
                      AND IsDeleted = 0
                      AND (ExecutedQty>0 OR ReceivedQty>0 OR ShippedQty>0 OR StatusFlag >1)";
        var rst = await Connection.ExecuteScalarAsync<int>(sql, new { independIds = independIds.ToList(), draftStatus = IndependentDemandStatusEnum.未确认 }, transaction: Transaction);
        return rst == 0;
    }
}
