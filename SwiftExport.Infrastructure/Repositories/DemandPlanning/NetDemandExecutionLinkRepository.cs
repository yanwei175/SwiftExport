using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

/// <summary>
/// 净需求执行关系仓储实现。
/// </summary>
public sealed class NetDemandExecutionLinkRepository(RepositoryBaseContext context)
    : INetDemandExecutionLinkRepository
{
    /// <summary>读取指定物料需求当前有效的执行关系，不再提供 Trace 历史查询。</summary>
    public async Task<IReadOnlyList<NetDemandExecutionLinkDto>> GetActiveByNetDemandIdsAsync(
        IEnumerable<Guid> netDemandIds,
        CancellationToken cancellationToken = default)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var sql = LinkSelect + @"
FROM [dbo].[DemandExecutionLink] l
LEFT JOIN [dbo].[PurchaseOrderLine] pol
  ON pol.[Uid]=l.[TargetLineId] AND l.[TargetType]=@PurchaseTargetType AND ISNULL(pol.[IsDeleted],0)=0
WHERE l.[NetDemandId] IN @Ids AND l.[LinkStatus]=@Active AND l.[IsDeleted]=0
ORDER BY l.[CreatedDate],l.[Uid];";
        var rows = await context.Session.Connection.QueryAsync<NetDemandExecutionLinkDto>(new CommandDefinition(
            sql,
            new { Ids = ids, Active = DemandExecutionLinkStatusEnum.Active, PurchaseTargetType = "PO" },
            context.Session.Transaction,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>按物料需求锁定活动执行关系，新链路不再要求计划分配存在。</summary>
    public async Task<IReadOnlyList<NetDemandExecutionLinkDto>> GetActiveByNetDemandIdsForUpdateAsync(
        IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<NetDemandExecutionLinkDto>(LinkSelect + @"
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN [dbo].[PurchaseOrderLine] pol
  ON pol.[Uid]=l.[TargetLineId] AND l.[TargetType]=@PurchaseTargetType AND ISNULL(pol.[IsDeleted],0)=0
WHERE l.[NetDemandId] IN @Ids AND l.[LinkStatus]=@Active AND l.[IsDeleted]=0
ORDER BY l.[CreatedDate],l.[Uid];",
            new { Ids = ids, Active = DemandExecutionLinkStatusEnum.Active, PurchaseTargetType = "PO" },
            context.Session.Transaction);
        return [.. rows];
    }

    /// <summary>按历史计划分配锁定活动执行关系，仅用于计划域既有回退校验。</summary>
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

    public async Task<IReadOnlyList<MaterialDemandRollbackBlockerDto>> GetActiveRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[NetDemandId],l.[TargetType],l.[TargetNo],l.[TargetLineId],l.[LinkStatus],
       CONCAT(N'净需求已关联 ',l.[TargetType],N' ',l.[TargetNo],N'，请先取消或完成反向处理。') AS [Message]
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK, HOLDLOCK)
WHERE l.[NetDemandId] IN @Ids AND l.[LinkStatus]=@Active AND l.[IsDeleted]=0
ORDER BY l.[TargetType],l.[TargetNo],l.[TargetLineId];";
        var rows = await context.Session.Connection.QueryAsync<MaterialDemandRollbackBlockerDto>(
            sql, new { Ids = ids, Active = DemandExecutionLinkStatusEnum.Active }, context.Session.Transaction);
        return [.. rows];
    }


    public async Task<IReadOnlyList<DemandExecutionLink>> GetByCommandItemIdsForUpdateAsync(
        IEnumerable<Guid> commandItemIds)
    {
        var ids = commandItemIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[Uid],l.[Version],l.[IsDeleted],l.[CreatedDate],l.[CreatedBy],l.[UpdatedDate],l.[UpdatedBy],
       l.[NetDemandId],l.[NetDemandAllocationId],l.[ExecutionRequestId],l.[CommandItemId],
       l.[TargetType],l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],l.[LinkStatus],l.[AllocatedQty],
       l.[ReleasedQty],l.[DemandRequiredDate],l.[PlannedDeliveryDate],
       l.[CancelledAt],l.[CancelledBy],l.[CancelledReason],l.[ReversedAt],l.[ReversedBy],l.[ReversedReason],l.[Remark]
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK,HOLDLOCK)
WHERE l.[CommandItemId] IN @Ids AND l.[IsDeleted]=0
ORDER BY l.[CommandItemId],l.[Uid];";
        var rows = await context.Session.Connection.QueryAsync<DemandExecutionLink>(
            sql, new { Ids = ids }, context.Session.Transaction);
        return [.. rows];
    }

    public async Task<int> InsertPurchaseLinksAsync(IEnumerable<DemandExecutionLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);
        var materialized = links.ToArray();
        if (materialized.Length == 0) return 0;
        if (materialized.Any(x => x is null || x.Uid == Guid.Empty))
            throw new ArgumentException("采购 Link 必须由 Application 预生成 Uid。", nameof(links));

        foreach (var link in materialized)
        {
            context.AuditStamp.StampForInsert(link);
            link.IsDeleted = false;
            if (link.Version <= 0) link.Version = 1;
        }

        const string sql = @"
INSERT INTO [dbo].[DemandExecutionLink]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [NetDemandId],[NetDemandAllocationId],[ExecutionRequestId],[CommandItemId],
 [TargetType],[TargetHeadId],[TargetNo],[TargetLineId],[LinkStatus],[AllocatedQty],[ReleasedQty],
 [DemandRequiredDate],[PlannedDeliveryDate],[CancelledAt],[CancelledBy],[CancelledReason],
 [ReversedAt],[ReversedBy],[ReversedReason],[Remark])
VALUES
(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @NetDemandId,@NetDemandAllocationId,@ExecutionRequestId,@CommandItemId,
 @TargetType,@TargetHeadId,@TargetNo,@TargetLineId,@LinkStatus,@AllocatedQty,@ReleasedQty,
 @DemandRequiredDate,@PlannedDeliveryDate,@CancelledAt,@CancelledBy,@CancelledReason,
 @ReversedAt,@ReversedBy,@ReversedReason,@Remark);";
        return await context.Session.Connection.ExecuteAsync(
            sql, materialized, context.Session.Transaction);
    }

    private const string LinkSelect = @"
SELECT l.[Uid],l.[NetDemandId],l.[NetDemandAllocationId],l.[LinkStatus],
       l.[TargetType],l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],pol.[LineNumber] AS [TargetLineNumber],
       l.[AllocatedQty]-l.[ReleasedQty] AS [AllocatedQty],l.[ReleasedQty],
       l.[AllocatedQty]-l.[ReleasedQty] AS [EffectiveQty],
       ISNULL((SELECT SUM(il.[Quantity]) FROM [dbo].[InboundDocumentLine] il
               INNER JOIN [dbo].[InboundDocument] ih ON ih.[Uid]=il.[InboundDocumentId]
               WHERE il.[DemandExecutionLinkId]=l.[Uid] AND ih.[PostingStatus]=1
                 AND il.[IsDeleted]=0 AND ih.[IsDeleted]=0),0) AS [CompletedQty],
       ISNULL((SELECT SUM(il.[Quantity]) FROM [dbo].[InboundDocumentLine] il
               INNER JOIN [dbo].[InboundDocument] ih ON ih.[Uid]=il.[InboundDocumentId]
               WHERE il.[DemandExecutionLinkId]=l.[Uid] AND ih.[PostingStatus]=1
                 AND il.[IsDeleted]=0 AND ih.[IsDeleted]=0),0) AS [ReceivedQty],
       l.[DemandRequiredDate],l.[PlannedDeliveryDate],l.[CancelledAt],l.[CancelledBy],l.[CancelledReason],l.[Remark] ";
}
