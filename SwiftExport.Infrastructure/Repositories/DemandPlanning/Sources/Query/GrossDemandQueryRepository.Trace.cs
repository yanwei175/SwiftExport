using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

/// <summary>来源需求只读追踪；各集合保留各自的事实粒度，避免多对多关系重复计数。</summary>
public sealed partial class GrossDemandQueryRepository
{
    /// <inheritdoc/>
    public async Task<GrossDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        // 历史 Link 仍可能对应正式收货；只审查本来源实际关联的采购行。
        var sql = """
            SELECT DISTINCT link.TargetLineId AS PurchaseLineId INTO #TracePurchaseLines
            FROM dbo.NetDemand net
            JOIN dbo.DemandExecutionLink link ON link.NetDemandId=net.Uid
            WHERE net.GrossDemandId=@Uid AND link.TargetType=N'PO';
            """ + InboundReceiptHistorySql.ScopedGuard + """
            SELECT g.Uid AS GrossDemandId,g.SourceType,g.SourceUid,g.SourceLineId,g.SourceNo,g.SourceLineNumber,
                   ISNULL(c.CustomerName,N'') AS CustomerName,g.SkuSnapshot AS Sku,
                   ISNULL(g.CustomerSkuSnapshot,N'') AS CustomerSku,ISNULL(g.UnitSnapshot,N'') AS Unit,
                   g.DemandQty,g.RequiredDate,g.LifecycleStatus,g.CustomerId,g.SourceVersion,
                   g.DemandBasisFingerprint
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.Customer c ON c.Uid=g.CustomerId AND c.IsDeleted=0
            WHERE g.Uid=@Uid AND g.IsDeleted=0;

            -- 拆解修订历史均展示；只有来源版本、指纹可证明的活动行才能汇总当前采购贡献。
            SELECT n.Uid AS NetDemandId,n.DecompositionRevision,n.LineNumber,n.LifecycleStatus,
                   n.SkuSnapshot AS Sku,ISNULL(n.MaterialTypeSnapshot,N'') AS MaterialType,n.NetQty,
                   CASE WHEN n.LifecycleStatus=0 AND n.IsDeleted=0
                             AND n.SourceDemandVersionSnapshot=g.Version
                             AND NULLIF(g.DemandBasisFingerprint,N'') IS NOT NULL
                             AND n.SourceDemandBasisFingerprintSnapshot=g.DemandBasisFingerprint
                             AND g.CustomerId IS NOT NULL AND customerMismatch.HasMismatch IS NULL
                             AND COALESCE(purchase.InvalidCount,0)=0
                             AND ((g.SourceType=N'MD' AND g.SourceVersion IS NULL AND n.SourceVersionSnapshot IS NULL)
                                  OR (g.SourceVersion IS NOT NULL AND n.SourceVersionSnapshot=g.SourceVersion))
                        THEN COALESCE(purchase.LinkedQty,0) ELSE NULL END AS ActivePurchaseQty,
                   CASE WHEN n.LifecycleStatus<>0 THEN N'历史拆解修订'
                        WHEN n.IsDeleted<>0 THEN N'已删除物料行'
                        WHEN g.CustomerId IS NULL OR customerMismatch.HasMismatch IS NOT NULL
                          OR COALESCE(purchase.InvalidCount,0)>0 THEN N'活动采购 Link 目标、客户或单位需核实'
                        WHEN n.SourceDemandVersionSnapshot IS NULL OR n.SourceDemandVersionSnapshot<>g.Version
                          OR NULLIF(g.DemandBasisFingerprint,N'') IS NULL
                          OR n.SourceDemandBasisFingerprintSnapshot IS NULL
                          OR n.SourceDemandBasisFingerprintSnapshot<>g.DemandBasisFingerprint
                          OR (g.SourceType<>N'MD' AND (g.SourceVersion IS NULL OR n.SourceVersionSnapshot IS NULL OR n.SourceVersionSnapshot<>g.SourceVersion))
                          OR (g.SourceType=N'MD' AND n.SourceVersionSnapshot IS NOT NULL)
                        THEN N'来源版本或需求指纹需核实'
                        ELSE N'' END AS ReviewReason
            FROM dbo.NetDemand n
            JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            LEFT JOIN dbo.Product p ON p.Uid=n.ProductId AND p.IsDeleted=0
            OUTER APPLY (
                SELECT SUM(CASE WHEN pol.Uid IS NOT NULL AND poh.Uid IS NOT NULL
                                       AND NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NOT NULL
                                       AND LTRIM(RTRIM(pol.UnitSnapshot))=NULLIF(LTRIM(RTRIM(materialUnit.UnitSnapshot)),N'')
                                THEN l.AllocatedQty-l.ReleasedQty ELSE 0 END) AS LinkedQty,
                       SUM(CASE WHEN pol.Uid IS NULL OR poh.Uid IS NULL
                                     OR poh.CustomerId IS NULL
                                     OR NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NULL
                                     OR LTRIM(RTRIM(pol.UnitSnapshot))<>NULLIF(LTRIM(RTRIM(materialUnit.UnitSnapshot)),N'')
                                     OR NULLIF(LTRIM(RTRIM(materialUnit.UnitSnapshot)),N'') IS NULL
                                THEN 1 ELSE 0 END) AS InvalidCount
                FROM dbo.DemandExecutionLink l
                -- 单位进入聚合内的本地关系，避免 SQL Server 聚合表达式混用外部引用。
                JOIN dbo.NetDemand materialUnit ON materialUnit.Uid=l.NetDemandId
                LEFT JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId AND pol.PurchaseOrderId=l.TargetHeadId AND pol.IsDeleted=0
                LEFT JOIN dbo.PurchaseOrderHead poh ON poh.Uid=l.TargetHeadId AND poh.IsDeleted=0 AND poh.LifecycleStatus<>9
                WHERE l.NetDemandId=n.Uid AND l.IsDeleted=0 AND l.LinkStatus=0
                  AND l.TargetType=N'PO' AND l.AllocatedQty>l.ReleasedQty
            ) purchase
            OUTER APPLY (
                SELECT TOP (1) 1 AS HasMismatch
                FROM dbo.DemandExecutionLink l
                JOIN dbo.PurchaseOrderHead poh ON poh.Uid=l.TargetHeadId AND poh.IsDeleted=0
                WHERE l.NetDemandId=n.Uid AND l.IsDeleted=0 AND l.LinkStatus=0
                  AND l.TargetType=N'PO' AND l.AllocatedQty>l.ReleasedQty
                  AND poh.CustomerId<>g.CustomerId
            ) customerMismatch
            WHERE n.GrossDemandId=@Uid
            ORDER BY n.DecompositionRevision DESC,n.LineNumber,n.Uid;

            -- Link 历史和采购目标同时展示；货好只属于整条采购行，不回分摊到来源需求。
            SELECT n.Uid AS NetDemandId,l.TargetHeadId AS PurchaseOrderId,l.TargetLineId AS PurchaseLineId,
                   CONVERT(bit,CASE WHEN poh.Uid IS NOT NULL AND pol.Uid IS NOT NULL THEN 1 ELSE 0 END) AS HasPurchaseTarget,
                   COALESCE(poh.OrderNo,l.TargetNo,N'') AS PurchaseOrderNo,ISNULL(pol.LineNumber,0) AS PurchaseLineNumber,
                   ISNULL(pol.Sku,N'') AS Sku,ISNULL(pol.UnitSnapshot,N'') AS Unit,
                   ISNULL(poh.ConfirmationStatus,0) AS ConfirmationStatus,ISNULL(poh.LifecycleStatus,9) AS LifecycleStatus,
                   l.AllocatedQty,l.ReleasedQty,l.AllocatedQty-l.ReleasedQty AS LinkedQty,ISNULL(pol.QTY,0) AS PurchaseLineQty,
                   latest.ReadyQty AS PurchaseLineReadyQty,COALESCE(receipts.ReceivedQty,0) AS PurchaseLineReceivedQty,
                   latest.ExpectedReadyDate,
                   CONVERT(bit,CASE WHEN EXISTS (
                       SELECT 1 FROM dbo.DemandExecutionLink other
                       JOIN dbo.NetDemand otherNet ON otherNet.Uid=other.NetDemandId
                       WHERE other.TargetLineId=l.TargetLineId AND other.NetDemandId<>n.Uid
                         AND otherNet.GrossDemandId<>@Uid AND other.IsDeleted=0 AND other.LinkStatus=0
                         AND other.AllocatedQty>other.ReleasedQty
                   ) THEN 1 ELSE 0 END) AS IsSharedPurchaseLine,
                   CASE WHEN l.LinkStatus<>0 OR l.IsDeleted<>0 THEN N'历史采购关联'
                        WHEN pol.Uid IS NULL OR poh.Uid IS NULL OR poh.LifecycleStatus=9 THEN N'采购目标需核实'
                        WHEN poh.CustomerId IS NULL OR g.CustomerId IS NULL OR poh.CustomerId<>g.CustomerId THEN N'客户归属需核实'
                        WHEN NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NULL
                          OR NULLIF(LTRIM(RTRIM(n.UnitSnapshot)),N'') IS NULL
                          OR LTRIM(RTRIM(pol.UnitSnapshot))<>NULLIF(LTRIM(RTRIM(n.UnitSnapshot)),N'')
                        THEN N'采购行与物料单位需核实'
                        WHEN n.LifecycleStatus<>0 OR n.SourceDemandVersionSnapshot IS NULL OR n.SourceDemandVersionSnapshot<>g.Version
                          OR NULLIF(g.DemandBasisFingerprint,N'') IS NULL
                          OR n.SourceDemandBasisFingerprintSnapshot IS NULL
                          OR n.SourceDemandBasisFingerprintSnapshot<>g.DemandBasisFingerprint
                          OR (g.SourceType<>N'MD' AND (g.SourceVersion IS NULL OR n.SourceVersionSnapshot IS NULL OR n.SourceVersionSnapshot<>g.SourceVersion))
                          OR (g.SourceType=N'MD' AND n.SourceVersionSnapshot IS NOT NULL)
                        THEN N'来源修订需核实' ELSE N'' END AS ReviewReason
            FROM dbo.NetDemand n
            JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            LEFT JOIN dbo.Product p ON p.Uid=n.ProductId AND p.IsDeleted=0
            JOIN dbo.DemandExecutionLink l ON l.NetDemandId=n.Uid
            LEFT JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId AND pol.PurchaseOrderId=l.TargetHeadId AND pol.IsDeleted=0
            LEFT JOIN dbo.PurchaseOrderHead poh ON poh.Uid=l.TargetHeadId AND poh.IsDeleted=0
            OUTER APPLY (
                SELECT TOP (1) report.ReadyQty,report.ExpectedReadyDate
                FROM dbo.SupplierProgressReport report
                WHERE report.PurchaseOrderLineId=pol.Uid AND report.IsDeleted=0
                ORDER BY report.Sequence DESC,report.Uid DESC
            ) latest
            OUTER APPLY (
                SELECT SUM(receiptLine.Quantity) AS ReceivedQty
                FROM dbo.InboundDocumentLine receiptLine
                JOIN dbo.InboundDocument receiptHead ON receiptHead.Uid=receiptLine.InboundDocumentId
                    AND receiptHead.IsDeleted=0 AND receiptHead.DocumentType=10 AND receiptHead.PostingStatus=1
                WHERE receiptLine.SourceLineId=pol.Uid AND receiptLine.IsDeleted=0
            ) receipts
            WHERE n.GrossDemandId=@Uid AND l.TargetType=N'PO'
            ORDER BY n.DecompositionRevision DESC,poh.OrderNo,pol.LineNumber,l.Uid;

            SELECT ph.Uid AS PackingListId,pl.Uid AS PackingListLineId,ph.PackingListNo,
                   pl.LineNumber,pl.Qty,ISNULL(pl.UnitSnapshot,N'') AS Unit,
                   CONVERT(bit,CASE WHEN ph.PostingStatus=1 THEN 1 ELSE 0 END) AS CountsTowardDelivery,
                   ph.PostedDate,ph.ReversedDate,
                    CASE WHEN ph.CustomerId IS NULL OR ph.CustomerId<>g.CustomerId OR g.CustomerId IS NULL THEN N'交付客户需核实'
                         WHEN ISNULL(pl.SourceTypeSnapshot,N'')<>g.SourceType OR ISNULL(pl.SourceDocumentNoSnapshot,N'')<>g.SourceNo
                           OR ISNULL(pl.SourceLineNumberSnapshot,-1)<>ISNULL(g.SourceLineNumber,-1)
                           OR ISNULL(pl.SkuSnapshot,N'')<>g.SkuSnapshot OR pl.IsNonStandardSnapshot<>g.IsNonStandard
                           OR ISNULL(pl.ProductId,'00000000-0000-0000-0000-000000000000')
                              <>ISNULL(g.ProductId,'00000000-0000-0000-0000-000000000000')
                           OR pl.Qty<=0 OR (ph.PostingStatus IN (1,8) AND ph.PostedDate IS NULL)
                           OR (ph.PostingStatus=8 AND ph.ReversedDate IS NULL)
                           THEN N'装箱来源或正式历史需核实'
                        WHEN NULLIF(LTRIM(RTRIM(pl.UnitSnapshot)),N'') IS NULL
                          OR NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N'') IS NULL
                          OR LTRIM(RTRIM(pl.UnitSnapshot))<>LTRIM(RTRIM(g.UnitSnapshot)) THEN N'交付单位需核实'
                        ELSE N'' END AS ReviewReason
            FROM dbo.PackingListLine pl
            JOIN dbo.PackingListHead ph ON ph.Uid=pl.PackingListHeadId AND ph.IsDeleted=0
            JOIN dbo.GrossDemand g ON g.Uid=pl.GrossDemandId
            WHERE pl.GrossDemandId=@Uid AND pl.IsDeleted=0 AND ph.PostingStatus IN (1,8)
            ORDER BY ph.PostedDate,ph.Uid,pl.LineNumber,pl.Uid;

            -- 被软删除的已过账行和无法定位单头的行不会出现在展示集合，仍必须阻断确定性已交量。
            SELECT COUNT(*) FROM dbo.PackingListLine pl
            LEFT JOIN dbo.PackingListHead ph ON ph.Uid=pl.PackingListHeadId
            JOIN dbo.GrossDemand g ON g.Uid=pl.GrossDemandId
            WHERE pl.GrossDemandId=@Uid AND (ph.Uid IS NULL
                OR (pl.IsDeleted=1 AND ph.PostingStatus IN (1,8))
                OR (pl.IsDeleted=0 AND (ph.IsDeleted=1 OR ph.PostingStatus NOT IN (0,1,8)
                    OR ph.CustomerId IS NULL OR ph.CustomerId<>g.CustomerId OR g.CustomerId IS NULL
                    OR ISNULL(pl.SourceTypeSnapshot,N'')<>g.SourceType OR ISNULL(pl.SourceDocumentNoSnapshot,N'')<>g.SourceNo
                    OR ISNULL(pl.SourceLineNumberSnapshot,-1)<>ISNULL(g.SourceLineNumber,-1)
                    OR ISNULL(pl.SkuSnapshot,N'')<>g.SkuSnapshot OR pl.IsNonStandardSnapshot<>g.IsNonStandard
                    OR ISNULL(pl.ProductId,'00000000-0000-0000-0000-000000000000')
                       <>ISNULL(g.ProductId,'00000000-0000-0000-0000-000000000000')
                    OR pl.Qty<=0 OR NULLIF(LTRIM(RTRIM(pl.UnitSnapshot)),N'') IS NULL
                    OR pl.UnitSnapshot<>g.UnitSnapshot
                    OR (ph.PostingStatus IN (1,8) AND ph.PostedDate IS NULL)
                    OR (ph.PostingStatus=8 AND ph.ReversedDate IS NULL))));
            DROP TABLE #TracePurchaseLines;
            """;
        using var multi = await Connection.QueryMultipleAsync(new CommandDefinition(
            sql, new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
        var header = await multi.ReadSingleOrDefaultAsync<TraceHeader>();
        if (header is null) return null;
        var detail = new GrossDemandTraceDetailDto
        {
            GrossDemandId = header.GrossDemandId,
            SourceType = header.SourceType,
            SourceUid = header.SourceUid,
            SourceNo = header.SourceNo,
            SourceLineNumber = header.SourceLineNumber,
            CustomerName = header.CustomerName,
            Sku = header.Sku,
            CustomerSku = header.CustomerSku,
            Unit = header.Unit,
            DemandQty = header.DemandQty,
            RequiredDate = header.RequiredDate,
            LifecycleStatus = header.LifecycleStatus,
            Materials = (await multi.ReadAsync<GrossDemandTraceMaterialDto>()).ToList(),
            Purchases = (await multi.ReadAsync<GrossDemandTracePurchaseDto>()).ToList(),
            Deliveries = (await multi.ReadAsync<GrossDemandTraceDeliveryDto>()).ToList()
        };
        var invalidPackingHistoryCount = await multi.ReadSingleAsync<int>();
        // 来源自身与全部关联箱单历史都可证明时才给出确定的交付净额。
        var sourceReviewReason = header.SourceUid == Guid.Empty || header.SourceLineId == Guid.Empty
            || string.IsNullOrWhiteSpace(header.SourceNo)
            || header.SourceType is not ("MD" or "SO" or "CR")
            || (header.SourceType == "MD" ? header.SourceVersion is not null : header.SourceVersion is null or <= 0)
            || header.DemandBasisFingerprint is not { Length: 64 }
            || header.DemandBasisFingerprint.Any(ch => !((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F')))
            ? "来源身份、版本或需求指纹需核实" : string.Empty;
        var deliveryReviewReason = header.CustomerId is null ? "来源客户缺失" :
            string.IsNullOrWhiteSpace(header.Unit) ? "来源单位缺失" :
            invalidPackingHistoryCount>0 || detail.Deliveries.Any(row => !string.IsNullOrEmpty(row.ReviewReason))
                ? "装箱历史客户、来源、数量或单位需核实" : string.Empty;
        detail.ReviewReason = string.Join("；", new[] { sourceReviewReason, deliveryReviewReason }
            .Where(reason => !string.IsNullOrEmpty(reason)));
        detail.DeliveredQty = string.IsNullOrEmpty(deliveryReviewReason)
            ? detail.Deliveries.Where(row => row.CountsTowardDelivery).Sum(row => row.Qty)
            : null;
        return detail;
    }

    /// <summary>仅用于接收追踪头查询的客户身份，不作为对外契约。</summary>
    private sealed class TraceHeader
    {
        public Guid GrossDemandId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public Guid SourceUid { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceVersion { get; set; }
        public string? DemandBasisFingerprint { get; set; }
        public string SourceNo { get; set; } = string.Empty;
        public int? SourceLineNumber { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string CustomerSku { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }
    }
}
