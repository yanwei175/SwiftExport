using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
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
       l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],
       l.[AllocatedQty]-l.[ReleasedQty] AS [AllocatedQty],l.[ReleasedQty],
       l.[AllocatedQty]-l.[ReleasedQty] AS [EffectiveQty],l.[CompletedQty],l.[ReceivedQty],
       poh.[Version] AS [TargetHeadVersion],poh.[LifecycleStatus] AS [TargetHeadLifecycleStatus],
       poh.[ConfirmationStatus] AS [TargetHeadConfirmationStatus],
       l.[CancelledAt],l.[CancelledBy],l.[CancelledReason],l.[ReversedAt],l.[ReversedBy],l.[ReversedReason],l.[Remark]
FROM [dbo].[DemandExecutionLink] l
LEFT JOIN [dbo].[PurchaseOrderHead] poh
  ON poh.[Uid]=l.[TargetHeadId] AND l.[TargetType]=N'PO' AND poh.[IsDeleted]=0
WHERE l.[DependentDemandId] IN @Ids AND l.[IsDeleted]=0
ORDER BY l.[CreatedDate],l.[Uid];";
        var rows = await context.Session.Connection.QueryAsync<DemandExecutionTraceItemDto>(new CommandDefinition(
            sql, new { Ids = ids }, context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<IReadOnlyList<DemandExecutionLink>> GetByCommandItemIdsForUpdateAsync(
        IEnumerable<Guid> commandItemIds)
    {
        var ids = commandItemIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT l.[Uid],l.[Version],l.[IsDeleted],l.[CreatedDate],l.[CreatedBy],l.[UpdatedDate],l.[UpdatedBy],
       l.[DependentDemandId] AS [NetDemandId],l.[NetDemandAllocationId],l.[ExecutionRequestId],l.[CommandItemId],
       l.[TargetType],l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],l.[LinkStatus],l.[AllocatedQty],
       l.[CompletedQty],l.[ReceivedQty],l.[DemandRequiredDate],l.[PlannedDeliveryDate],
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
 [DependentDemandId],[NetDemandAllocationId],[ExecutionRequestId],[CommandItemId],
 [TargetType],[TargetHeadId],[TargetNo],[TargetLineId],[LinkStatus],[AllocatedQty],[ReleasedQty],[CompletedQty],[ReceivedQty],
 [DemandRequiredDate],[PlannedDeliveryDate],[CancelledAt],[CancelledBy],[CancelledReason],
 [ReversedAt],[ReversedBy],[ReversedReason],[Remark])
VALUES
(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @NetDemandId,@NetDemandAllocationId,@ExecutionRequestId,@CommandItemId,
 @TargetType,@TargetHeadId,@TargetNo,@TargetLineId,@LinkStatus,@AllocatedQty,@ReleasedQty,@CompletedQty,@ReceivedQty,
 @DemandRequiredDate,@PlannedDeliveryDate,@CancelledAt,@CancelledBy,@CancelledReason,
 @ReversedAt,@ReversedBy,@ReversedReason,@Remark);";
        return await context.Session.Connection.ExecuteAsync(
            sql, materialized, context.Session.Transaction);
    }

    public async Task<SourcePurchaseTraceDto?> GetPurchaseTraceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("来源类型不能为空。", nameof(sourceType));
        if (sourceUid == Guid.Empty)
            throw new ArgumentException("来源 Uid 不能为空。", nameof(sourceUid));

        const string sql = @"
SELECT TOP(1) d.[SourceType],d.[SourceUid],d.[SourceNo]
FROM [dbo].[IndependentDemand] d
WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid AND d.[IsDeleted]=0
ORDER BY d.[SourceNo],d.[Uid];

SELECT d.[Uid] AS [GrossDemandId],nd.[Uid] AS [NetDemandId],l.[NetDemandAllocationId] AS [AllocationId],
       l.[Uid] AS [DemandExecutionLinkId],l.[LinkStatus],d.[SourceLineNumber],nd.[Sku],nd.[NetQty] AS [NetDemandQty],
       l.[AllocatedQty] AS [OriginalLinkQty],l.[ReleasedQty],
       l.[AllocatedQty]-l.[ReleasedQty] AS [LinkQty],l.[DemandRequiredDate],l.[PlannedDeliveryDate],
       h.[Uid] AS [PurchaseOrderId],h.[OrderNo] AS [PurchaseOrderNo],
       h.[LifecycleStatus] AS [PurchaseLifecycleStatus],h.[ConfirmationStatus] AS [PurchaseConfirmationStatus],
       line.[Uid] AS [PurchaseOrderLineId],line.[LineNumber] AS [PurchaseOrderLineNumber],
       h.[SupplierId],h.[SupplierCode],h.[SupplierName]
FROM [dbo].[IndependentDemand] d
INNER JOIN [dbo].[DependentDemand] nd
        ON nd.[IndependentDemandId]=d.[Uid] AND nd.[IsDeleted]=0
INNER JOIN [dbo].[DemandExecutionLink] l
        ON l.[DependentDemandId]=nd.[Uid] AND l.[NetDemandAllocationId] IS NOT NULL
       AND l.[TargetType]=@PurchaseTargetType AND l.[IsDeleted]=0
INNER JOIN [dbo].[NetDemandAllocation] allocation
        ON allocation.[Uid]=l.[NetDemandAllocationId] AND allocation.[NetDemandId]=nd.[Uid]
INNER JOIN [dbo].[PurchaseOrderHead] h
        ON h.[Uid]=l.[TargetHeadId] AND h.[IsDeleted]=0
INNER JOIN [dbo].[PurchaseOrderLine] line
        ON line.[Uid]=l.[TargetLineId] AND line.[PurchaseOrderId]=h.[Uid] AND line.[IsDeleted]=0
WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid AND d.[IsDeleted]=0
ORDER BY d.[SourceLineNumber],nd.[LineNumber],line.[LineNumber],l.[Uid];";

        using var multi = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new
            {
                SourceType = sourceType.Trim(),
                SourceUid = sourceUid,
                PurchaseTargetType = "PO"
            },
            context.Session.Transaction,
            cancellationToken: cancellationToken));
        var trace = await multi.ReadSingleOrDefaultAsync<SourcePurchaseTraceDto>();
        if (trace is null) return null;
        trace.Lines = (await multi.ReadAsync<SourcePurchaseTraceLineDto>()).ToList();
        return trace;
    }

    public async Task<IReadOnlyList<PurchaseDraftLifecycleLinkFactDto>> GetPurchaseLinksByHeadAsync(
        Guid purchaseOrderId,
        bool withUpdLock = false,
        CancellationToken cancellationToken = default)
    {
        if (purchaseOrderId == Guid.Empty)
            throw new ArgumentException("采购头 Uid 不能为空。", nameof(purchaseOrderId));
        var lockHint = withUpdLock ? "WITH (UPDLOCK,HOLDLOCK)" : "WITH (NOLOCK)";
        var sql = $"""
            SELECT l.Uid AS LinkId,l.NetDemandAllocationId AS AllocationId,
                   l.TargetLineId AS PurchaseOrderLineId,gd.SourceLineNumber,l.PlannedDeliveryDate,
                   l.AllocatedQty,l.ReleasedQty,l.AllocatedQty-l.ReleasedQty AS EffectiveQty
            FROM dbo.DemandExecutionLink l {lockHint}
            INNER JOIN dbo.DependentDemand nd ON nd.Uid=l.DependentDemandId AND nd.IsDeleted=0
            INNER JOIN dbo.IndependentDemand gd ON gd.Uid=nd.IndependentDemandId AND gd.IsDeleted=0
            WHERE l.TargetHeadId=@PurchaseOrderId AND l.TargetType=@PurchaseTargetType
              AND l.LinkStatus=@Active AND l.IsDeleted=0
            ORDER BY l.PlannedDeliveryDate DESC,gd.SourceLineNumber DESC,l.NetDemandAllocationId DESC,l.Uid DESC;
            """;
        var rows = await context.Session.Connection.QueryAsync<PurchaseDraftLifecycleLinkFactDto>(
            new CommandDefinition(sql, new
            {
                PurchaseOrderId = purchaseOrderId,
                PurchaseTargetType = "PO",
                Active = DemandExecutionLinkStatusEnum.Active,
            }, context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public Task<int> CancelPurchaseLinksByHeadAsync(Guid purchaseOrderId, string reason)
    {
        const string sql = """
            UPDATE dbo.DemandExecutionLink
            SET ReleasedQty=AllocatedQty,LinkStatus=@Cancelled,CancelledAt=@Now,CancelledBy=@UserName,
                CancelledReason=@Reason,UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
            WHERE TargetHeadId=@PurchaseOrderId AND TargetType=@PurchaseTargetType
              AND NetDemandAllocationId IS NOT NULL AND ExecutionRequestId IS NOT NULL
              AND CommandItemId IS NOT NULL AND LinkStatus=@Active AND IsDeleted=0;
            """;
        return context.Session.Connection.ExecuteAsync(sql, new
        {
            PurchaseOrderId = purchaseOrderId,
            PurchaseTargetType = "PO",
            Active = DemandExecutionLinkStatusEnum.Active,
            Cancelled = DemandExecutionLinkStatusEnum.Cancelled,
            Reason = reason,
            Now = DateTime.Now,
            UserName = context.CurrentUser.UserName,
        }, context.Session.Transaction);
    }

    public async Task<int> ApplyPurchaseLinkReleasesAsync(
        IEnumerable<PurchaseDraftLinkReleaseDto> releases,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(releases);
        var affected = 0;
        const string sql = """
            UPDATE dbo.DemandExecutionLink
            SET ReleasedQty=ReleasedQty+@ReleaseQty,
                LinkStatus=CASE WHEN AllocatedQty-(ReleasedQty+@ReleaseQty)=0 THEN @Cancelled ELSE @Active END,
                ReversedAt=@Now,ReversedBy=@UserName,ReversedReason=@Reason,
                CancelledAt=CASE WHEN AllocatedQty-(ReleasedQty+@ReleaseQty)=0 THEN @Now ELSE CancelledAt END,
                CancelledBy=CASE WHEN AllocatedQty-(ReleasedQty+@ReleaseQty)=0 THEN @UserName ELSE CancelledBy END,
                CancelledReason=CASE WHEN AllocatedQty-(ReleasedQty+@ReleaseQty)=0 THEN @Reason ELSE CancelledReason END,
                UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
            WHERE Uid=@LinkId AND LinkStatus=@Active AND IsDeleted=0
              AND AllocatedQty-ReleasedQty=@CurrentEffectiveQty
              AND @ReleaseQty>0 AND @ReleaseQty<=AllocatedQty-ReleasedQty;
            """;
        foreach (var release in releases)
        {
            affected += await context.Session.Connection.ExecuteAsync(sql, new
            {
                release.LinkId,
                release.ReleaseQty,
                release.CurrentEffectiveQty,
                Active = DemandExecutionLinkStatusEnum.Active,
                Cancelled = DemandExecutionLinkStatusEnum.Cancelled,
                Reason = reason,
                Now = DateTime.Now,
                UserName = context.CurrentUser.UserName,
            }, context.Session.Transaction);
        }
        return affected;
    }

    private const string LinkSelect = @"
SELECT l.[Uid],l.[DependentDemandId] AS [NetDemandId],l.[NetDemandAllocationId],l.[LinkStatus],
       l.[TargetType],l.[TargetHeadId],l.[TargetNo],l.[TargetLineId],pol.[LineNumber] AS [TargetLineNumber],
       l.[AllocatedQty]-l.[ReleasedQty] AS [AllocatedQty],l.[ReleasedQty],
       l.[AllocatedQty]-l.[ReleasedQty] AS [EffectiveQty],
       CAST(0 AS decimal(18,4)) AS [CompletedQty],CAST(0 AS decimal(18,4)) AS [ReceivedQty],
       l.[DemandRequiredDate],l.[PlannedDeliveryDate],l.[CancelledAt],l.[CancelledBy],l.[CancelledReason],l.[Remark] ";
}
