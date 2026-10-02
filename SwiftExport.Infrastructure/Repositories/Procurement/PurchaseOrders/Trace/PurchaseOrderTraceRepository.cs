using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace;

/// <summary>按采购单身份读取来源、反馈和正式收货历史，保持各证据集合独立，避免一对多倍增。</summary>
public sealed class PurchaseOrderTraceRepository(RepositoryBaseContext context) : IPurchaseOrderTraceRepository
{
    /// <inheritdoc/>
    public async Task<PurchaseOrderTraceDto?> GetAsync(Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT Uid AS PurchaseLineId INTO #TracePurchaseLines
            FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@PurchaseOrderId;
            """ + InboundReceiptHistorySql.ScopedGuard + TraceSql + "DROP TABLE #TracePurchaseLines;",
            new { PurchaseOrderId = purchaseOrderId, PurchaseReceipt = (int)InboundDocumentTypeEnum.PurchaseReceipt,
                FormalPostingStatuses = new[] { (int)InboundPostingStatusEnum.Posted, (int)InboundPostingStatusEnum.Reversed } },
            context.Session.Transaction, cancellationToken: cancellationToken));
        var trace = await grid.ReadSingleOrDefaultAsync<PurchaseOrderTraceDto>();
        if (trace is null) return null;

        var lines = (await grid.ReadAsync<PurchaseOrderTraceLineDto>()).ToList();
        var links = (await grid.ReadAsync<PurchaseOrderTraceLinkDto>()).ToList();
        var reports = (await grid.ReadAsync<PurchaseOrderTraceProgressDto>()).ToList();
        var receipts = (await grid.ReadAsync<PurchaseOrderTraceReceiptDto>()).ToList();
        trace.Operations = (await grid.ReadAsync<PurchaseOrderTraceOperationDto>()).ToList();

        foreach (var line in lines)
        {
            line.Links = links.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            line.ProgressHistory = reports.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            line.Receipts = receipts.Where(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId).ToList();
            // OriginKind 只记录创建类别；独立采购可后续补关联，按实际 Link 核实来源证据。
            if (trace.OriginKind != 2 && line.Links.Count == 0 && !line.IsDeleted)
                line.ReviewReason = "当前采购行未找到需求 Link，请核实来源。";
            else if (line.Links.Any(x => x.NeedsReview))
                line.ReviewReason = "部分需求来源身份、版本、指纹或贡献数量需核实。";
            if (line.Receipts.Any(x =>
                (string.IsNullOrWhiteSpace(x.UnitSnapshot) || x.UnitSnapshot != line.UnitSnapshot)))
                line.ReviewReason = "正式入库单位与采购行不一致，净收货量需核实。";
            else
                line.FormalReceivedQty = line.Receipts.Sum(x => x.NetQuantity);
        }
        trace.Lines = lines;
        return trace;
    }

    // 历史 Link、反馈和反向入库分别读取；正式净额只由 Posted 贡献，Reversed 保留证据但贡献零。
    private const string TraceSql = """
        SELECT h.Uid AS PurchaseOrderId,h.OrderNo,h.SupplierName,h.OriginKind,
               h.ConfirmationStatus,h.LifecycleStatus
        FROM dbo.PurchaseOrderHead h
        WHERE h.Uid=@PurchaseOrderId AND ISNULL(h.IsDeleted,0)=0;

        SELECT l.Uid AS PurchaseOrderLineId,l.LineNumber,l.IsDeleted,l.Sku,l.UnitSnapshot,
               l.QTY AS Quantity,l.UnlinkedQty,l.PlannedDeliveryDate,
               latest.ReadyQty AS LatestReadyQty
        FROM dbo.PurchaseOrderLine l
        OUTER APPLY
        (
            SELECT TOP (1) f.ReadyQty FROM dbo.SupplierProgressReport f
            WHERE f.PurchaseOrderLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0
            ORDER BY f.Sequence DESC,f.Uid DESC
        ) latest
        WHERE l.PurchaseOrderId=@PurchaseOrderId
        ORDER BY l.LineNumber,l.Uid;

        SELECT link.Uid AS LinkId,link.TargetLineId AS PurchaseOrderLineId,link.NetDemandId,link.IsDeleted,
               n.GrossDemandId,link.LinkStatus,link.AllocatedQty,link.ReleasedQty,
               link.AllocatedQty-link.ReleasedQty AS EffectiveQty,
               g.SourceType,g.SourceUid,g.SourceLineId,g.SourceNo,g.SourceLineNumber,
               n.DecompositionRevision,g.SourceVersion,n.SourceDemandVersionSnapshot,n.SourceVersionSnapshot,
               g.DemandBasisFingerprint,n.SourceDemandBasisFingerprintSnapshot AS DemandBasisFingerprintSnapshot,
               g.UnitSnapshot,
               link.DemandRequiredDate,link.CancelledAt,link.CancelledBy,link.CancelledReason,
               CONVERT(bit,CASE WHEN n.Uid IS NULL OR g.Uid IS NULL
                   OR g.SourceUid IS NULL OR g.SourceUid='00000000-0000-0000-0000-000000000000'
                   OR g.SourceLineId IS NULL OR g.SourceLineId='00000000-0000-0000-0000-000000000000'
                   OR n.SourceDemandVersionSnapshot IS NULL OR n.SourceDemandVersionSnapshot<>g.Version
                   OR (g.SourceType=N'MD' AND (g.SourceVersion IS NOT NULL OR n.SourceVersionSnapshot IS NOT NULL))
                   OR (g.SourceType<>N'MD' AND (g.SourceVersion IS NULL OR n.SourceVersionSnapshot IS NULL
                       OR n.SourceVersionSnapshot<>g.SourceVersion))
                   OR NULLIF(g.DemandBasisFingerprint,N'') IS NULL
                   OR NULLIF(n.SourceDemandBasisFingerprintSnapshot,N'') IS NULL
                   OR n.SourceDemandBasisFingerprintSnapshot<>g.DemandBasisFingerprint
                   OR NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NULL
                   OR COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                       CASE WHEN n.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') END) IS NULL
                   OR LTRIM(RTRIM(l.UnitSnapshot))<>COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                       CASE WHEN n.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') END)
                   OR link.TargetHeadId<>l.PurchaseOrderId
                   OR link.AllocatedQty<0 OR link.ReleasedQty<0 OR link.ReleasedQty>link.AllocatedQty
                   THEN 1 ELSE 0 END) AS NeedsReview,
               CASE WHEN n.Uid IS NULL OR g.Uid IS NULL THEN N'需求来源记录缺失。'
                    WHEN g.SourceUid IS NULL OR g.SourceUid='00000000-0000-0000-0000-000000000000'
                      OR g.SourceLineId IS NULL OR g.SourceLineId='00000000-0000-0000-0000-000000000000'
                      THEN N'需求来源身份缺失。'
                    WHEN n.SourceDemandVersionSnapshot IS NULL OR n.SourceDemandVersionSnapshot<>g.Version
                      OR (g.SourceType=N'MD' AND (g.SourceVersion IS NOT NULL OR n.SourceVersionSnapshot IS NOT NULL))
                      OR (g.SourceType<>N'MD' AND (g.SourceVersion IS NULL OR n.SourceVersionSnapshot IS NULL
                          OR n.SourceVersionSnapshot<>g.SourceVersion))
                      OR NULLIF(g.DemandBasisFingerprint,N'') IS NULL
                      OR NULLIF(n.SourceDemandBasisFingerprintSnapshot,N'') IS NULL
                      OR n.SourceDemandBasisFingerprintSnapshot<>g.DemandBasisFingerprint
                      THEN N'来源版本或需求指纹缺失，或与当前来源不一致。'
                    WHEN NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NULL
                      OR COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                          CASE WHEN n.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') END) IS NULL
                      OR LTRIM(RTRIM(l.UnitSnapshot))<>COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                          CASE WHEN n.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') END)
                      THEN N'物料与采购行单位不一致。'
                    WHEN link.TargetHeadId<>l.PurchaseOrderId THEN N'Link 采购头行身份不一致。'
                    WHEN link.AllocatedQty<0 OR link.ReleasedQty<0 OR link.ReleasedQty>link.AllocatedQty
                      THEN N'需求贡献或释放数量异常。'
                    ELSE NULL END AS ReviewReason
        FROM dbo.DemandExecutionLink link
        INNER JOIN dbo.PurchaseOrderLine l ON l.Uid=link.TargetLineId AND l.PurchaseOrderId=@PurchaseOrderId
        LEFT JOIN dbo.NetDemand n ON n.Uid=link.NetDemandId
        LEFT JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
        LEFT JOIN dbo.Product p ON p.Uid=n.ProductId AND p.IsDeleted=0
        WHERE link.TargetType=N'PO'
        ORDER BY l.LineNumber,link.DemandRequiredDate,link.Uid;

        SELECT f.Uid,f.PurchaseOrderLineId,f.Sequence,f.ArrangementStatus,f.ReadyQty,
               f.ExpectedReadyDate,f.SupplierReportedDate,f.Note,f.IsCorrection,f.CorrectionReason,
               f.CreatedDate,f.CreatedBy
        FROM dbo.SupplierProgressReport f
        INNER JOIN dbo.PurchaseOrderLine l ON l.Uid=f.PurchaseOrderLineId AND l.PurchaseOrderId=@PurchaseOrderId
        WHERE ISNULL(f.IsDeleted,0)=0
        ORDER BY l.LineNumber,f.Sequence DESC,f.Uid DESC;

        SELECT h.Uid AS InboundDocumentId,l.Uid AS InboundLineId,
               l.SourceLineId AS PurchaseOrderLineId,h.DocumentNo,h.PostingStatus,
               l.Quantity,CASE WHEN h.PostingStatus=1 THEN l.Quantity ELSE 0 END AS NetQuantity,
               l.UnitSnapshot,h.WarehouseNameSnapshot,h.ReceiptDate,h.PostedDate,h.PostedBy,
               h.ReversedDate,h.ReversedBy,h.ReverseReason,h.TriggerPackingListId
        FROM dbo.InboundDocumentLine l
        INNER JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId
        INNER JOIN dbo.PurchaseOrderLine p ON p.Uid=l.SourceLineId AND p.PurchaseOrderId=@PurchaseOrderId
        WHERE h.DocumentType=@PurchaseReceipt AND h.PostingStatus IN @FormalPostingStatuses
          AND ISNULL(h.IsDeleted,0)=0 AND ISNULL(l.IsDeleted,0)=0
        ORDER BY h.ReceiptDate,h.Uid,l.LineNumber,l.Uid;

        SELECT log.Uid,log.OperationName,log.OperationType,log.Reason,log.Summary,
               log.UserNameSnapshot AS UserName,log.OccurredDate
        FROM dbo.BusinessOperationLog log
        WHERE log.AggregateUid=@PurchaseOrderId AND ISNULL(log.IsDeleted,0)=0
        ORDER BY log.OccurredDate DESC,log.Uid DESC;
        """;
}
