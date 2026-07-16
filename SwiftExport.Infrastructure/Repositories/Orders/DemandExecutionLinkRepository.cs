using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 净需求执行关系仓储实现。
/// </summary>
public class DemandExecutionLinkRepository(RepositoryBaseContext context)
    : IDemandExecutionLinkRepository
{
    public async Task<IReadOnlyList<GrossDemandRollbackBlockerDto>> GetActiveRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[DependentDemandId] AS [NetDemandId],l.[TargetType],l.[TargetNo],l.[TargetLineId],l.[LinkStatus],
       CONCAT(N'净需求已关联 ',l.[TargetType],N' ',l.[TargetNo],N'，请先取消或完成反向处理。') AS [Message]
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK, HOLDLOCK)
WHERE l.[DependentDemandId] IN @Ids AND l.[LinkStatus]=@Active AND l.[IsDeleted]=0
ORDER BY l.[TargetType],l.[TargetNo],l.[TargetLineId];";
        var rows = await context.Session.Connection.QueryAsync<GrossDemandRollbackBlockerDto>(
            sql, new { Ids = ids, Active = DemandExecutionLinkStatusEnum.Active }, context.Session.Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<DemandExecutionTraceItemDto>> GetTraceByNetDemandIdsAsync(
        IEnumerable<Guid> netDemandIds,
        CancellationToken cancellationToken = default)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[Uid],l.[DependentDemandId] AS [NetDemandId],l.[LinkStatus],l.[TargetType],
       l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],l.[AllocatedQty],l.[CompletedQty],l.[ReceivedQty],
       l.[CancelledAt],l.[CancelledBy],l.[CancelledReason],l.[ReversedAt],l.[ReversedBy],l.[ReversedReason],l.[Remark]
FROM [dbo].[DemandExecutionLink] l
WHERE l.[DependentDemandId] IN @Ids AND l.[IsDeleted]=0
ORDER BY l.[CreatedDate],l.[Uid];";
        var rows = await context.Session.Connection.QueryAsync<DemandExecutionTraceItemDto>(new CommandDefinition(
            sql, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }
}
