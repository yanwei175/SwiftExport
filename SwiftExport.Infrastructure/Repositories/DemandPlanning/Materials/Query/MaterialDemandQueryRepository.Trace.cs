using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;

/// <summary>把单条物料的历史 Link 与当前收货覆盖分开读取，避免采购行共享造成重复归属。</summary>
public sealed partial class MaterialDemandQueryRepository
{
    /// <inheritdoc/>
    public async Task<MaterialDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        // 当前物料的历史 Link 同样需要反向证明；其他采购行的异常不阻断本页。
        var sql = """
            SELECT DISTINCT TargetLineId AS PurchaseLineId INTO #TracePurchaseLines
            FROM dbo.DemandExecutionLink
            WHERE NetDemandId=@Uid AND TargetType=N'PO';
            """ + InboundReceiptHistorySql.ScopedGuard + """
            SELECT n.Uid AS NetDemandId,n.GrossDemandId,g.SourceType,g.SourceNo,g.SourceLineNumber,
                   n.DecompositionRevision,n.LineNumber,n.LifecycleStatus,n.SkuSnapshot AS Sku,
                   NULLIF(LTRIM(RTRIM(n.UnitSnapshot)),N'') AS Unit,
                   n.NetQty,n.RequiredDate,n.RolledBackAt,n.RolledBackBy,n.RolledBackReason,
                   n.ParentNetDemandId,n.RootNetDemandId,
                   g.LifecycleStatus AS SourceLifecycleStatus,g.CustomerId,g.Version AS SourceDemandVersion,
                   g.SourceVersion,g.DemandBasisFingerprint,n.SourceDemandVersionSnapshot,
                   n.SourceVersionSnapshot,n.SourceDemandBasisFingerprintSnapshot
            FROM dbo.NetDemand n
            JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            LEFT JOIN dbo.Product p ON p.Uid=n.ProductId AND p.IsDeleted=0
            WHERE n.Uid=@Uid AND n.IsDeleted=0;

            SELECT sibling.Uid AS NetDemandId,sibling.ParentNetDemandId,sibling.DecompositionRevision,
                   sibling.LineNumber,sibling.LifecycleStatus,sibling.SkuSnapshot AS Sku,sibling.NetQty
            FROM dbo.NetDemand sibling
            JOIN dbo.NetDemand selected ON selected.GrossDemandId=sibling.GrossDemandId
            WHERE selected.Uid=@Uid AND sibling.IsDeleted=0
            ORDER BY sibling.DecompositionRevision DESC,sibling.LineNumber,sibling.Uid;

            -- 当前覆盖只用仍有效且身份、客户、单位均可证明的 Link；排序是查询投影，不写入历史归属。
            WITH eligible AS
            (
                SELECT l.Uid AS LinkId,l.TargetLineId,l.AllocatedQty-l.ReleasedQty AS LinkedQty,
                       l.DemandRequiredDate,g.SourceLineNumber,pol.QTY AS PurchaseLineQty,
                       pol.UnitSnapshot AS PurchaseLineUnit,
                       CASE WHEN n.LifecycleStatus=0 AND n.IsDeleted=0 AND g.LifecycleStatus=0 AND g.IsDeleted=0
                                  AND g.CustomerId IS NOT NULL AND poh.CustomerId=g.CustomerId
                                  AND n.SourceDemandVersionSnapshot=g.Version
                                  AND NULLIF(g.DemandBasisFingerprint,N'') IS NOT NULL
                                  AND n.SourceDemandBasisFingerprintSnapshot=g.DemandBasisFingerprint
                                  AND ((g.SourceType=N'MD' AND g.SourceVersion IS NULL AND n.SourceVersionSnapshot IS NULL)
                                    OR (g.SourceType<>N'MD' AND g.SourceVersion IS NOT NULL
                                        AND n.SourceVersionSnapshot=g.SourceVersion))
                                  AND NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NOT NULL
                                  AND LTRIM(RTRIM(pol.UnitSnapshot))=NULLIF(LTRIM(RTRIM(n.UnitSnapshot)),N'')
                                  AND poh.LifecycleStatus=0 AND poh.IsDeleted=0 AND pol.IsDeleted=0
                            THEN 1 ELSE 0 END AS IsValid
                FROM dbo.DemandExecutionLink l
                JOIN dbo.NetDemand n ON n.Uid=l.NetDemandId
                JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
                LEFT JOIN dbo.Product p ON p.Uid=n.ProductId AND p.IsDeleted=0
                JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId AND pol.PurchaseOrderId=l.TargetHeadId
                JOIN dbo.PurchaseOrderHead poh ON poh.Uid=l.TargetHeadId
                WHERE l.TargetType=N'PO' AND l.LinkStatus=0 AND l.IsDeleted=0
                  AND l.AllocatedQty>l.ReleasedQty
                  AND l.TargetLineId IN (SELECT TargetLineId FROM dbo.DemandExecutionLink WHERE NetDemandId=@Uid AND TargetType=N'PO')
            ), ranked AS
            (
                SELECT e.*,
                       SUM(e.LinkedQty) OVER (PARTITION BY e.TargetLineId) AS TotalLinkedQty,
                       SUM(CASE WHEN e.IsValid=0 THEN 1 ELSE 0 END) OVER (PARTITION BY e.TargetLineId) AS InvalidCount,
                       SUM(e.LinkedQty) OVER (PARTITION BY e.TargetLineId
                           ORDER BY CASE WHEN e.DemandRequiredDate IS NULL THEN 1 ELSE 0 END,e.DemandRequiredDate,
                                    CASE WHEN e.SourceLineNumber IS NULL THEN 1 ELSE 0 END,e.SourceLineNumber,e.LinkId
                           ROWS UNBOUNDED PRECEDING)-e.LinkedQty AS EarlierLinkedQty
                FROM eligible e
            )
            SELECT r.LinkId,r.TargetLineId,
                   CASE WHEN r.InvalidCount=0 AND r.TotalLinkedQty<=r.PurchaseLineQty
                                  AND r.PurchaseLineQty>=0 AND COALESCE(receipts.ReceivedQty,0)>=0
                                  AND invalidReceipts.InvalidReceiptCount=0
                        THEN CASE WHEN COALESCE(receipts.ReceivedQty,0)<=r.EarlierLinkedQty THEN 0
                                  WHEN COALESCE(receipts.ReceivedQty,0)>=r.EarlierLinkedQty+r.LinkedQty THEN r.LinkedQty
                                  ELSE COALESCE(receipts.ReceivedQty,0)-r.EarlierLinkedQty END END AS CurrentReceiptCoverageQty
            INTO #MaterialReceiptCoverage
            FROM ranked r
            OUTER APPLY
            (
                SELECT SUM(receiptLine.Quantity) AS ReceivedQty
                FROM dbo.InboundDocumentLine receiptLine
                JOIN dbo.InboundDocument receiptHead ON receiptHead.Uid=receiptLine.InboundDocumentId
                     AND receiptHead.IsDeleted=0 AND receiptHead.DocumentType=10 AND receiptHead.PostingStatus=1
                WHERE receiptLine.SourceLineId=r.TargetLineId AND receiptLine.IsDeleted=0
            ) receipts
            OUTER APPLY
            (
                SELECT COUNT(*) AS InvalidReceiptCount
                FROM dbo.InboundDocumentLine receiptLine
                JOIN dbo.InboundDocument receiptHead ON receiptHead.Uid=receiptLine.InboundDocumentId
                     AND receiptHead.IsDeleted=0 AND receiptHead.DocumentType=10 AND receiptHead.PostingStatus=1
                WHERE receiptLine.SourceLineId=r.TargetLineId AND receiptLine.IsDeleted=0
                  AND (NULLIF(LTRIM(RTRIM(receiptLine.UnitSnapshot)),N'') IS NULL
                       OR LTRIM(RTRIM(receiptLine.UnitSnapshot))<>LTRIM(RTRIM(r.PurchaseLineUnit)))
            ) invalidReceipts;

            SELECT l.Uid AS LinkId,l.TargetHeadId AS PurchaseOrderId,l.TargetLineId AS PurchaseLineId,
                   CONVERT(bit,CASE WHEN poh.Uid IS NOT NULL AND pol.Uid IS NOT NULL THEN 1 ELSE 0 END) AS HasPurchaseTarget,
                   COALESCE(poh.OrderNo,l.TargetNo,N'') AS PurchaseOrderNo,ISNULL(pol.LineNumber,0) AS PurchaseLineNumber,
                   ISNULL(supplier.SupplierName,N'') AS ActualSupplierName,
                   ISNULL(poh.ConfirmationStatus,0) AS ConfirmationStatus,
                   ISNULL(poh.LifecycleStatus,9) AS LifecycleStatus,
                   CONVERT(bit,CASE WHEN l.LinkStatus=0 AND l.IsDeleted=0 AND l.AllocatedQty>l.ReleasedQty
                                          AND n.LifecycleStatus=0 THEN 1 ELSE 0 END) AS IsActiveLink,
                   l.AllocatedQty,l.ReleasedQty,l.AllocatedQty-l.ReleasedQty AS LinkedQty,
                   ISNULL(pol.QTY,0) AS PurchaseLineQty,ISNULL(pol.UnitSnapshot,N'') AS Unit,
                   latest.ReadyQty AS PurchaseLineReadyQty,latest.ExpectedReadyDate,
                   COALESCE(receipts.ReceivedQty,0) AS PurchaseLineReceivedQty,
                   CASE WHEN l.LinkStatus=0 AND l.IsDeleted=0 AND n.LifecycleStatus=0
                        THEN coverage.CurrentReceiptCoverageQty END AS CurrentReceiptCoverageQty,
                   CASE WHEN COALESCE(totals.InvalidCount,0)=0 AND invalidReceipts.InvalidReceiptCount=0 THEN
                        CASE WHEN COALESCE(receipts.ReceivedQty,0)>COALESCE(totals.CoveredQty,0)
                             THEN COALESCE(receipts.ReceivedQty,0)-COALESCE(totals.CoveredQty,0) ELSE 0 END END
                        AS ReceiptOutsideCurrentCoverageQty,
                   COALESCE(history.HistoricalLinkedQty,0) AS HistoricalLinkedQty,
                   CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM dbo.DemandExecutionLink other
                       WHERE other.TargetLineId=l.TargetLineId AND other.NetDemandId<>@Uid
                         AND other.TargetType=N'PO' AND other.LinkStatus=0 AND other.IsDeleted=0
                         AND other.AllocatedQty>other.ReleasedQty) THEN 1 ELSE 0 END) AS IsSharedPurchaseLine,
                   CASE WHEN l.LinkStatus<>0 OR l.IsDeleted<>0 OR n.LifecycleStatus<>0 THEN N'历史采购关联'
                        WHEN poh.Uid IS NULL OR pol.Uid IS NULL OR poh.LifecycleStatus<>0 THEN N'采购目标需核实'
                        WHEN poh.CustomerId IS NULL OR g.CustomerId IS NULL OR poh.CustomerId<>g.CustomerId THEN N'客户归属需核实'
                        WHEN NULLIF(LTRIM(RTRIM(n.UnitSnapshot)),N'') IS NULL
                          OR NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NULL
                          OR NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'')<>NULLIF(LTRIM(RTRIM(n.UnitSnapshot)),N'')
                        THEN N'采购单位需核实'
                        WHEN invalidReceipts.InvalidReceiptCount>0 THEN N'入库单位需核实'
                        WHEN coverage.CurrentReceiptCoverageQty IS NULL THEN N'来源修订或采购行贡献需核实'
                        ELSE N'' END AS ReviewReason
            FROM dbo.DemandExecutionLink l
            JOIN dbo.NetDemand n ON n.Uid=l.NetDemandId
            JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            LEFT JOIN dbo.Product p ON p.Uid=n.ProductId AND p.IsDeleted=0
            LEFT JOIN dbo.PurchaseOrderHead poh ON poh.Uid=l.TargetHeadId AND poh.IsDeleted=0
            LEFT JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId AND pol.PurchaseOrderId=l.TargetHeadId AND pol.IsDeleted=0
            LEFT JOIN dbo.Supplier supplier ON supplier.Uid=poh.SupplierId
            LEFT JOIN #MaterialReceiptCoverage coverage ON coverage.LinkId=l.Uid
            OUTER APPLY (SELECT SUM(cover2.CurrentReceiptCoverageQty) AS CoveredQty,
                               SUM(CASE WHEN cover2.CurrentReceiptCoverageQty IS NULL THEN 1 ELSE 0 END) AS InvalidCount
                         FROM #MaterialReceiptCoverage cover2 WHERE cover2.TargetLineId=l.TargetLineId) totals
            OUTER APPLY (SELECT SUM(old.AllocatedQty-old.ReleasedQty) AS HistoricalLinkedQty
                         FROM dbo.DemandExecutionLink old
                         JOIN dbo.NetDemand oldNet ON oldNet.Uid=old.NetDemandId
                         WHERE old.TargetLineId=l.TargetLineId AND old.TargetType=N'PO'
                           AND (old.LinkStatus<>0 OR old.IsDeleted<>0 OR oldNet.LifecycleStatus<>0)) history
            OUTER APPLY (SELECT TOP (1) report.ReadyQty,report.ExpectedReadyDate
                         FROM dbo.SupplierProgressReport report
                         WHERE report.PurchaseOrderLineId=pol.Uid AND report.IsDeleted=0
                         ORDER BY report.Sequence DESC,report.Uid DESC) latest
            OUTER APPLY (SELECT SUM(receiptLine.Quantity) AS ReceivedQty
                         FROM dbo.InboundDocumentLine receiptLine
                         JOIN dbo.InboundDocument receiptHead ON receiptHead.Uid=receiptLine.InboundDocumentId
                           AND receiptHead.IsDeleted=0 AND receiptHead.DocumentType=10 AND receiptHead.PostingStatus=1
                         WHERE receiptLine.SourceLineId=pol.Uid AND receiptLine.IsDeleted=0) receipts
            OUTER APPLY (SELECT COUNT(*) AS InvalidReceiptCount
                         FROM dbo.InboundDocumentLine receiptLine
                         JOIN dbo.InboundDocument receiptHead ON receiptHead.Uid=receiptLine.InboundDocumentId
                           AND receiptHead.IsDeleted=0 AND receiptHead.DocumentType=10 AND receiptHead.PostingStatus=1
                         WHERE receiptLine.SourceLineId=pol.Uid AND receiptLine.IsDeleted=0
                           AND (NULLIF(LTRIM(RTRIM(receiptLine.UnitSnapshot)),N'') IS NULL
                                OR LTRIM(RTRIM(receiptLine.UnitSnapshot))<>LTRIM(RTRIM(pol.UnitSnapshot)))) invalidReceipts
            WHERE l.NetDemandId=@Uid AND l.TargetType=N'PO'
            ORDER BY poh.OrderNo,pol.LineNumber,l.Uid;
            DROP TABLE #MaterialReceiptCoverage;
            DROP TABLE #TracePurchaseLines;
            """;

        using var multi = await Connection.QueryMultipleAsync(new CommandDefinition(
            sql, new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
        var header = await multi.ReadSingleOrDefaultAsync<TraceHeader>();
        if (header is null) return null;
        var tree = (await multi.ReadAsync<MaterialDemandTraceTreeItemDto>()).ToList();
        var purchases = (await multi.ReadAsync<MaterialDemandTracePurchaseDto>()).ToList();
        var reason = GetSourceReviewReason(header);
        if (string.IsNullOrEmpty(reason) && purchases.Any(row => row.IsActiveLink && !string.IsNullOrEmpty(row.ReviewReason)))
            reason = "采购目标、客户、单位或来源修订需核实";
        var associated = header.LifecycleStatus == NetDemandLifecycleStatusEnum.Active && string.IsNullOrEmpty(reason)
            ? purchases.Where(row => row.IsActiveLink).Sum(row => row.LinkedQty) : (decimal?)null;
        if (associated > header.NetQty) reason = "采购承接超量需核实";
        var coverage = associated.HasValue && string.IsNullOrEmpty(reason)
            ? purchases.Where(row => row.IsActiveLink).Sum(row => row.CurrentReceiptCoverageQty ?? 0m)
            : (decimal?)null;
        return new MaterialDemandTraceDetailDto
        {
            NetDemandId = header.NetDemandId, GrossDemandId = header.GrossDemandId,
            SourceType = header.SourceType, SourceNo = header.SourceNo,
            SourceLineNumber = header.SourceLineNumber, DecompositionRevision = header.DecompositionRevision,
            LineNumber = header.LineNumber, LifecycleStatus = header.LifecycleStatus,
            Sku = header.Sku, Unit = header.Unit, NetQty = header.NetQty, RequiredDate = header.RequiredDate,
            RolledBackAt = header.RolledBackAt, RolledBackBy = header.RolledBackBy,
            RolledBackReason = header.RolledBackReason, ParentNetDemandId = header.ParentNetDemandId,
            RootNetDemandId = header.RootNetDemandId, AssociatedPurchaseQty = associated,
            CurrentReceiptCoverageQty = coverage, ReviewReason = reason, TreeItems = tree, Purchases = purchases
        };
    }

    /// <summary>老来源缺少版本、指纹或单位时不借物料行推断可信身份。</summary>
    private static string GetSourceReviewReason(TraceHeader header)
    {
        if (header.LifecycleStatus != NetDemandLifecycleStatusEnum.Active) return "历史拆解修订";
        if (header.SourceLifecycleStatus != GrossDemandLifecycleStatusEnum.Active || header.CustomerId is null)
            return "来源状态或客户需核实";
        if (header.SourceDemandVersionSnapshot is null
            || header.SourceDemandVersionSnapshot != header.SourceDemandVersion
            || string.IsNullOrWhiteSpace(header.DemandBasisFingerprint)
            || header.SourceDemandBasisFingerprintSnapshot != header.DemandBasisFingerprint
            || (header.SourceType == "MD" && (header.SourceVersion is not null || header.SourceVersionSnapshot is not null))
            || (header.SourceType != "MD" && (header.SourceVersion is null
                || header.SourceVersionSnapshot != header.SourceVersion)))
            return "来源版本或需求指纹需核实";
        if (string.IsNullOrWhiteSpace(header.Unit)) return "物料单位需核实";
        return header.NetQty <= 0 ? "物料需求数量需核实" : string.Empty;
    }

    /// <summary>Repository 内部承载来源版本和指纹证据，不暴露给前端。</summary>
    private sealed class TraceHeader : MaterialDemandTraceDetailDto
    {
        public GrossDemandLifecycleStatusEnum SourceLifecycleStatus { get; set; }
        public Guid? CustomerId { get; set; }
        public int SourceDemandVersion { get; set; }
        public int? SourceDemandVersionSnapshot { get; set; }
        public int? SourceVersion { get; set; }
        public int? SourceVersionSnapshot { get; set; }
        public string? DemandBasisFingerprint { get; set; }
        public string? SourceDemandBasisFingerprintSnapshot { get; set; }
    }
}
