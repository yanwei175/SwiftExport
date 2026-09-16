using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
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
    /// <summary>按物料需求锁定活动执行关系，新链路不再要求计划分配存在。</summary>
    public async Task<IReadOnlyList<PurchaseDemandCommitmentDto>> GetActiveByNetDemandIdsForUpdateAsync(
        IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<PurchaseDemandCommitmentDto>(@"
SELECT l.Uid,l.NetDemandId,l.AllocatedQty-l.ReleasedQty AS AllocatedQty
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK,HOLDLOCK)
WHERE l.[NetDemandId] IN @Ids
  AND l.[TargetType]=@PurchaseTargetType
  AND l.[AllocatedQty]>l.[ReleasedQty]
  AND l.[IsDeleted]=0
ORDER BY l.[CreatedDate],l.[Uid];",
            new { Ids = ids, PurchaseTargetType = "PO" },
            context.Session.Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<MaterialDemandRollbackBlockerDto>> GetRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[NetDemandId],l.[TargetType],l.[TargetNo],l.[TargetLineId],l.[LinkStatus],
       CONCAT(N'净需求已关联 ',l.[TargetType],N' ',l.[TargetNo],N'，请先取消或完成反向处理。') AS [Message]
FROM [dbo].[DemandExecutionLink] l WITH (UPDLOCK, HOLDLOCK)
WHERE l.[NetDemandId] IN @Ids AND l.[AllocatedQty]>l.[ReleasedQty] AND l.[IsDeleted]=0
ORDER BY l.[TargetType],l.[TargetNo],l.[TargetLineId];";
        var rows = await context.Session.Connection.QueryAsync<MaterialDemandRollbackBlockerDto>(
            sql, new { Ids = ids }, context.Session.Transaction);
        return [.. rows];
    }


    public async Task<IReadOnlyList<DemandExecutionLink>> GetByCommandItemIdsForUpdateAsync(
        IEnumerable<Guid> commandItemIds)
    {
        var ids = commandItemIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[Uid],l.[Version],l.[IsDeleted],l.[CreatedDate],l.[CreatedBy],l.[UpdatedDate],l.[UpdatedBy],
       l.[NetDemandId],l.[ExecutionRequestId],l.[CommandItemId],
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
        if (materialized.Any(x => x.TargetType != "PO" || x.AllocatedQty <= 0m
            || x.ReleasedQty < 0m || x.ReleasedQty > x.AllocatedQty || x.IsDeleted))
            throw new ArgumentException("采购 Link 的目标类型、贡献数量或删除状态无效。", nameof(links));

        foreach (var link in materialized)
        {
            context.AuditStamp.StampForInsert(link);
            link.IsDeleted = false;
            if (link.Version <= 0) link.Version = 1;
        }

        const string sql = @"
INSERT INTO [dbo].[DemandExecutionLink]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [NetDemandId],[ExecutionRequestId],[CommandItemId],
 [TargetType],[TargetHeadId],[TargetNo],[TargetLineId],[LinkStatus],[AllocatedQty],[ReleasedQty],
 [DemandRequiredDate],[PlannedDeliveryDate],[CancelledAt],[CancelledBy],[CancelledReason],
 [ReversedAt],[ReversedBy],[ReversedReason],[Remark])
VALUES
(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @NetDemandId,@ExecutionRequestId,@CommandItemId,
 @TargetType,@TargetHeadId,@TargetNo,@TargetLineId,@LinkStatus,@AllocatedQty,@ReleasedQty,
 @DemandRequiredDate,@PlannedDeliveryDate,@CancelledAt,@CancelledBy,@CancelledReason,
 @ReversedAt,@ReversedBy,@ReversedReason,@Remark);";
        return await context.Session.Connection.ExecuteAsync(
            sql, materialized, context.Session.Transaction);
    }

}
