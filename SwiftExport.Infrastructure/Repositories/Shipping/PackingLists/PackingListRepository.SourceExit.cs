using Dapper;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>核验来源退出时的有效装箱单交付事实。</summary>
public sealed partial class PackingListRepository
{
    /// <inheritdoc/>
    public async Task<SourceDeliveryExitAssessment> AssessSourceExitForUpdateAsync(
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default)
    {
        var ids = grossDemandIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return new SourceDeliveryExitAssessment();
        const string sql = """
            ;WITH Related AS (
                SELECT l.Uid AS LineUid,l.IsDeleted AS LineIsDeleted,l.PackingListHeadId,
                       h.Uid AS HeadUid,h.IsDeleted AS HeadIsDeleted,h.LifecycleStatus,h.PostingStatus,
                       h.ReversedDate
                FROM dbo.PackingListLine l WITH (UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.PackingListHead h WITH (UPDLOCK,HOLDLOCK) ON h.Uid=l.PackingListHeadId
                WHERE l.GrossDemandId IN @Ids
            ), RelatedHeads AS (
                SELECT DISTINCT HeadUid FROM Related WHERE HeadUid IS NOT NULL
            )
            SELECT
              (SELECT COUNT(DISTINCT PackingListHeadId) FROM Related
               WHERE LineIsDeleted=0 AND HeadIsDeleted=0
                 AND LifecycleStatus=@ActiveLifecycle AND PostingStatus=@Unposted) AS ActiveDraftCount,
              (SELECT COUNT(DISTINCT PackingListHeadId) FROM Related
               WHERE LineIsDeleted=0 AND HeadIsDeleted=0 AND PostingStatus=@Posted) AS PostedPackingListCount,
              (SELECT COUNT(1) FROM dbo.OutboundDocument o WITH (UPDLOCK,HOLDLOCK)
               INNER JOIN RelatedHeads rh ON rh.HeadUid=o.TriggerPackingListId
               WHERE o.IsDeleted=0 AND o.DocumentType=@FinalCustomerShipment
                 AND o.PostingStatus=@OutboundPosted) AS PostedCustomerOutboundCount,
              ((SELECT COUNT(1) FROM Related
                WHERE LineIsDeleted=0 AND (
                    HeadUid IS NULL OR HeadIsDeleted<>0
                    OR PostingStatus NOT IN (@Unposted,@Posted,@Reversed)
                    OR LifecycleStatus NOT IN (@ActiveLifecycle,@ClosedLifecycle,@VoidedLifecycle)
                    OR (PostingStatus=@Unposted AND LifecycleStatus=@ClosedLifecycle)
                    OR (PostingStatus=@Reversed AND ReversedDate IS NULL)))
               +(SELECT COUNT(1) FROM dbo.OutboundDocument o WITH (UPDLOCK,HOLDLOCK)
                 INNER JOIN RelatedHeads rh ON rh.HeadUid=o.TriggerPackingListId
                 WHERE o.IsDeleted=0 AND o.DocumentType=@FinalCustomerShipment
                   AND o.PostingStatus NOT IN (@OutboundPosted,@OutboundReversed))) AS InvalidHistoryCount;
            """;
        return await context.Session.Connection.QuerySingleAsync<SourceDeliveryExitAssessment>(Command(sql, new
        {
            Ids = ids,
            ActiveLifecycle = PackingListLifecycleStatusEnum.Active,
            ClosedLifecycle = PackingListLifecycleStatusEnum.Closed,
            VoidedLifecycle = PackingListLifecycleStatusEnum.Voided,
            Unposted = PackingListPostingStatusEnum.Unposted,
            Posted = PackingListPostingStatusEnum.Posted,
            Reversed = PackingListPostingStatusEnum.Reversed,
            FinalCustomerShipment = OutboundDocumentTypeEnum.FinalCustomerShipment,
            OutboundPosted = OutboundPostingStatusEnum.Posted,
            OutboundReversed = OutboundPostingStatusEnum.Reversed
        }, cancellationToken));
    }

}
