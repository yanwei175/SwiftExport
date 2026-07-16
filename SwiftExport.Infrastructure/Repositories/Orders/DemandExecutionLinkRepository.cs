using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
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
    public async Task<IReadOnlyList<NetDemandExecutionLinkDto>> GetByNetDemandIdsAsync(
        IEnumerable<Guid> netDemandIds,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var statusFilter = activeOnly ? "AND l.[LinkStatus]=@Active" : string.Empty;
        var sql = LinkSelect + $@"
FROM [dbo].[DemandExecutionLink] l
LEFT JOIN [dbo].[PurchaseOrderLine] pol
  ON pol.[Uid]=l.[TargetLineId] AND l.[TargetType]=@PurchaseTargetType AND ISNULL(pol.[IsDeleted],0)=0
WHERE l.[DependentDemandId] IN @Ids AND l.[IsDeleted]=0 {statusFilter}
ORDER BY l.[CreatedDate],l.[Uid];";
        var rows = await context.Session.Connection.QueryAsync<NetDemandExecutionLinkDto>(new CommandDefinition(
            sql,
            new { Ids = ids, Active = DemandExecutionLinkStatusEnum.Active, PurchaseTargetType = "PO" },
            context.Session.Transaction,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<IReadOnlyList<NetDemandExecutionLinkDto>> GetActiveByAllocationIdsForUpdateAsync(
        IEnumerable<Guid> allocationIds)
    {
        var ids = allocationIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<NetDemandExecutionLinkDto>(LinkSelect + @"
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN [dbo].[PurchaseOrderLine] pol
  ON pol.[Uid]=l.[TargetLineId] AND l.[TargetType]=@PurchaseTargetType AND ISNULL(pol.[IsDeleted],0)=0
WHERE l.[NetDemandAllocationId] IN @Ids AND l.[LinkStatus]=@Active AND l.[IsDeleted]=0
ORDER BY l.[CreatedDate],l.[Uid];",
            new { Ids = ids, Active = DemandExecutionLinkStatusEnum.Active, PurchaseTargetType = "PO" },
            context.Session.Transaction);
        return [.. rows];
    }
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

    private const string LinkSelect = @"
SELECT l.[Uid],l.[DependentDemandId] AS [NetDemandId],l.[NetDemandAllocationId],l.[LinkStatus],
       l.[TargetType],l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],pol.[LineNumber] AS [TargetLineNumber],
       l.[AllocatedQty],CAST(0 AS decimal(18,4)) AS [CompletedQty],CAST(0 AS decimal(18,4)) AS [ReceivedQty],
       l.[DemandRequiredDate],l.[PlannedDeliveryDate],l.[CancelledAt],l.[CancelledBy],l.[CancelledReason],l.[Remark] ";
}
