using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>销售订单履约查询；采购、货好与箱单分别保持各自的事实粒度。</summary>
public sealed partial class SalesOrderQueryRepository
{
    /// <summary>按单据读取所有活动行及其可证明的执行证据，复用管理页正式交付可信性计算。</summary>
    public async Task<SalesFulfillmentDetailDto?> GetFulfillmentAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT h.Uid,h.Version,h.DocumentStatus,h.ConfirmationStatus,h.CustomerId,h.OrderNo,h.DeliveryDate
            INTO #CandidateOrders FROM dbo.SalesOrderHeader h WHERE h.Uid=@Uid AND h.IsDeleted=0;
            {SalesOrderFulfillmentQuerySql.CreateLineFacts}

            SELECT h.Uid AS SalesOrderId,h.OrderNo,ISNULL(h.CustomerCode,N'') AS CustomerCode,
                   ISNULL(c.CustomerName,N'') AS CustomerName,ISNULL(h.InvoiceNo,N'') AS InvoiceNo,
                   h.DocumentStatus,h.ConfirmationStatus,h.OrderDate,h.DeliveryDate
            FROM #CandidateOrders candidate
            JOIN dbo.SalesOrderHeader h ON h.Uid=candidate.Uid
            LEFT JOIN dbo.Customer c ON c.Uid=h.CustomerId AND c.IsDeleted=0;

            -- 只把可信的来源身份传给后续采购、装箱证据查询；超发仍可展示其真实来源历史。
            SELECT f.SalesOrderLineId,f.LineNumber,ISNULL(l.Sku,N'') AS Sku,
                   ISNULL(l.CustomerSku,N'') AS CustomerSku,f.Qty AS OrderedQty,
                   ISNULL(f.UnitSnapshot,N'') AS UnitSnapshot,f.EffectiveDueDate,
                   CASE WHEN f.ReviewReason IS NULL OR f.ReviewReason=N'超发需核实'
                        THEN g.Uid END AS GrossDemandId,
                   CASE WHEN f.ReviewReason IS NULL OR f.ReviewReason=N'超发需核实'
                        THEN g.DemandQty END AS GrossDemandQty,
                   CASE WHEN f.ReviewReason IS NULL OR f.ReviewReason=N'超发需核实'
                        THEN g.UnitSnapshot END AS GrossDemandUnitSnapshot,
                   CASE WHEN f.ReviewReason IS NULL OR f.ReviewReason=N'超发需核实'
                        THEN g.Version END AS GrossDemandVersion,
                   CASE WHEN f.ReviewReason IS NOT NULL AND f.ReviewReason<>N'超发需核实' THEN N'需核实'
                        WHEN g.Uid IS NULL AND h.ConfirmationStatus=@Pending THEN N'待确认'
                        WHEN g.Uid IS NULL THEN N'未关联' ELSE N'已关联' END AS SourceState,
                   f.ShippedQty,f.UnshippedQty,
                   CASE WHEN f.UnshippedQty IS NULL THEN NULL
                        WHEN h.DocumentStatus=@OrderClosed THEN CONVERT(decimal(18,4),0)
                        ELSE f.UnshippedQty END AS AllowedFurtherDeliveryQty,
                   ISNULL(f.ReviewReason,N'') AS ReviewReason
            INTO #FulfillmentLines
            FROM #SalesLineFacts f
            JOIN dbo.SalesOrderLine l ON l.Uid=f.SalesOrderLineId
            JOIN #CandidateOrders h ON h.Uid=f.SalesOrderId
            OUTER APPLY (
                SELECT TOP (1) ranked.Uid,ranked.DemandQty,ranked.UnitSnapshot,ranked.Version
                FROM (
                    SELECT demand.Uid,demand.LifecycleStatus,demand.SourceVersion,
                           demand.DemandQty,demand.UnitSnapshot,demand.Version,
                           SUM(CASE WHEN demand.LifecycleStatus=@DemandActive THEN 1 ELSE 0 END) OVER () AS ActiveCount,
                           COUNT(*) OVER () AS AllCount,
                           CASE WHEN SUM(CASE WHEN demand.LifecycleStatus=@DemandActive THEN 1 ELSE 0 END) OVER ()>0
                                THEN SUM(CASE WHEN demand.LifecycleStatus=@DemandActive THEN 1 ELSE 0 END) OVER ()
                                ELSE COUNT(*) OVER () END AS CandidateCount
                    FROM dbo.GrossDemand demand
                    WHERE demand.SourceType=@SalesSourceType AND demand.SourceUid=h.Uid
                      AND demand.SourceLineId=l.Uid AND demand.IsDeleted=0
                      AND demand.LifecycleStatus IN (@DemandActive,@DemandVoided)
                ) ranked
                ORDER BY CASE WHEN ranked.LifecycleStatus=@DemandActive THEN 0 ELSE 1 END,
                         ranked.SourceVersion DESC,ranked.Uid
            ) g;
            SELECT SalesOrderLineId,LineNumber,Sku,CustomerSku,OrderedQty,UnitSnapshot,
                   EffectiveDueDate,GrossDemandId,GrossDemandQty,GrossDemandUnitSnapshot,
                   GrossDemandVersion,SourceState,ShippedQty,UnshippedQty,
                   AllowedFurtherDeliveryQty,ReviewReason
            FROM #FulfillmentLines ORDER BY LineNumber,SalesOrderLineId;

            -- Link 的目标头行均需可定位，采购反馈保留采购行范围，不分摊到销售数量。
            SELECT fl.SalesOrderLineId,po.Uid AS PurchaseOrderId,pol.Uid AS PurchaseOrderLineId,
                   pol.LineNumber AS PurchaseLineNumber,po.OrderNo AS PurchaseOrderNo,
                   pol.Sku,ISNULL(pol.UnitSnapshot,N'') AS UnitSnapshot,
                   po.ConfirmationStatus,po.LifecycleStatus,
                   CASE WHEN po.CustomerId IS NULL OR po.CustomerId<>gross.CustomerId
                              OR MAX(CASE WHEN NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NULL
                                  OR COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                                      CASE WHEN net.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(gross.UnitSnapshot)),N'') END) IS NULL
                                  OR LTRIM(RTRIM(pol.UnitSnapshot))<>COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                                      CASE WHEN net.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(gross.UnitSnapshot)),N'') END)
                                  THEN 1 ELSE 0 END)>0
                        THEN NULL ELSE SUM(link.AllocatedQty-link.ReleasedQty) END AS LinkedQty,
                   pol.QTY AS PurchaseLineQty,
                   CASE WHEN po.CustomerId IS NULL OR po.CustomerId<>gross.CustomerId
                        THEN N'采购客户归属需核实'
                        WHEN MAX(CASE WHEN NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NULL
                            OR COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                                CASE WHEN net.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(gross.UnitSnapshot)),N'') END) IS NULL
                            OR LTRIM(RTRIM(pol.UnitSnapshot))<>COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                                CASE WHEN net.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(gross.UnitSnapshot)),N'') END)
                            THEN 1 ELSE 0 END)>0
                         THEN N'采购行与物料单位需核实'
                        ELSE N'' END AS ReviewReason,
                   CONVERT(bit,CASE WHEN EXISTS (
                       SELECT 1 FROM dbo.DemandExecutionLink otherLink
                       JOIN dbo.NetDemand otherNet ON otherNet.Uid=otherLink.NetDemandId
                           AND otherNet.IsDeleted=0 AND otherNet.LifecycleStatus=@NetActive
                       JOIN dbo.GrossDemand otherGross ON otherGross.Uid=otherNet.GrossDemandId
                           AND otherGross.IsDeleted=0 AND otherGross.LifecycleStatus=@DemandActive
                       WHERE otherLink.TargetType=N'PO' AND otherLink.TargetLineId=pol.Uid
                         AND otherLink.IsDeleted=0 AND otherLink.LinkStatus=@LinkActive
                         AND otherLink.AllocatedQty>otherLink.ReleasedQty
                         AND otherNet.GrossDemandId<>fl.GrossDemandId
                   ) THEN 1 ELSE 0 END) AS IsSharedPurchaseLine,
                   CONVERT(bit,CASE WHEN latest.Uid IS NULL THEN 0 ELSE 1 END) AS HasProgress,
                   latest.ReadyQty AS PurchaseLineReadyQty,latest.ExpectedReadyDate,latest.SupplierReportedDate
            FROM #FulfillmentLines fl
            JOIN dbo.GrossDemand gross ON gross.Uid=fl.GrossDemandId AND gross.LifecycleStatus=@DemandActive
            JOIN dbo.NetDemand net ON net.GrossDemandId=gross.Uid AND net.IsDeleted=0
                AND net.LifecycleStatus=@NetActive
                AND net.SourceDemandVersionSnapshot=gross.Version
                AND net.SourceVersionSnapshot=gross.SourceVersion
                AND net.SourceDemandBasisFingerprintSnapshot=gross.DemandBasisFingerprint
            LEFT JOIN dbo.Product p ON p.Uid=net.ProductId AND p.IsDeleted=0
            JOIN dbo.DemandExecutionLink link ON link.NetDemandId=net.Uid AND link.IsDeleted=0
                AND link.LinkStatus=@LinkActive AND link.TargetType=N'PO'
                AND link.AllocatedQty>link.ReleasedQty
            JOIN dbo.PurchaseOrderLine pol ON pol.Uid=link.TargetLineId AND pol.IsDeleted=0
                AND pol.PurchaseOrderId=link.TargetHeadId
            JOIN dbo.PurchaseOrderHead po ON po.Uid=link.TargetHeadId AND po.IsDeleted=0
                AND po.LifecycleStatus<>@PurchaseVoided
            OUTER APPLY (
                SELECT TOP (1) report.Uid,report.ReadyQty,report.ExpectedReadyDate,report.SupplierReportedDate
                FROM dbo.SupplierProgressReport report
                WHERE report.PurchaseOrderLineId=pol.Uid AND report.IsDeleted=0
                ORDER BY report.Sequence DESC,report.Uid DESC
            ) latest
            GROUP BY fl.SalesOrderLineId,fl.GrossDemandId,gross.CustomerId,gross.UnitSnapshot,
                     po.Uid,pol.Uid,pol.LineNumber,
                     po.OrderNo,po.CustomerId,pol.Sku,pol.UnitSnapshot,po.ConfirmationStatus,po.LifecycleStatus,pol.QTY,
                     latest.Uid,latest.ReadyQty,latest.ExpectedReadyDate,latest.SupplierReportedDate
            ORDER BY po.OrderNo,pol.LineNumber,pol.Uid;

            SELECT fl.SalesOrderLineId,ph.Uid AS PackingListId,pl.Uid AS PackingListLineId,
                   ph.PackingListNo,pl.LineNumber,pl.Qty,ISNULL(pl.UnitSnapshot,N'') AS UnitSnapshot,
                   CONVERT(bit,CASE WHEN ph.PostingStatus=@Posted THEN 1 ELSE 0 END) AS CountsTowardDelivery,
                   ph.PostedDate,ph.ReversedDate
            FROM #FulfillmentLines fl
            JOIN dbo.PackingListLine pl ON pl.GrossDemandId=fl.GrossDemandId AND pl.IsDeleted=0
            JOIN dbo.PackingListHead ph ON ph.Uid=pl.PackingListHeadId AND ph.IsDeleted=0
            WHERE ph.PostingStatus IN (@Posted,@Reversed)
            ORDER BY ph.PostedDate,ph.Uid,pl.LineNumber,pl.Uid;

            -- 活动 Link 的版本或目标证据异常不能被解释为“没有采购关联”。
            SELECT DISTINCT fl.SalesOrderLineId
            FROM #FulfillmentLines fl
            JOIN dbo.GrossDemand gross ON gross.Uid=fl.GrossDemandId AND gross.LifecycleStatus=@DemandActive
            JOIN dbo.NetDemand net ON net.GrossDemandId=gross.Uid AND net.IsDeleted=0
            LEFT JOIN dbo.Product p ON p.Uid=net.ProductId AND p.IsDeleted=0
            JOIN dbo.DemandExecutionLink link ON link.NetDemandId=net.Uid AND link.IsDeleted=0
                AND link.LinkStatus=@LinkActive AND link.TargetType=N'PO'
                AND link.AllocatedQty>link.ReleasedQty
            LEFT JOIN dbo.PurchaseOrderLine pol ON pol.Uid=link.TargetLineId
                AND pol.IsDeleted=0 AND pol.PurchaseOrderId=link.TargetHeadId
            LEFT JOIN dbo.PurchaseOrderHead po ON po.Uid=link.TargetHeadId AND po.IsDeleted=0
            WHERE net.LifecycleStatus<>@NetActive
                OR net.SourceDemandVersionSnapshot IS NULL
                OR net.SourceDemandVersionSnapshot<>gross.Version
                OR net.SourceVersionSnapshot IS NULL
                OR net.SourceVersionSnapshot<>gross.SourceVersion
                OR net.SourceDemandBasisFingerprintSnapshot IS NULL
                OR net.SourceDemandBasisFingerprintSnapshot<>gross.DemandBasisFingerprint
                OR pol.Uid IS NULL OR po.Uid IS NULL OR po.LifecycleStatus=@PurchaseVoided
                OR NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N'') IS NULL
                OR COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                    CASE WHEN net.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(gross.UnitSnapshot)),N'') END) IS NULL
                OR LTRIM(RTRIM(pol.UnitSnapshot))<>COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),
                    CASE WHEN net.ParentNetDemandId IS NULL THEN NULLIF(LTRIM(RTRIM(gross.UnitSnapshot)),N'') END);
            """;
        var command = new CommandDefinition(sql, new
        {
            Uid = uid, SalesSourceType = DocumentTypeStaticsEnum.SalesOrder,
            OrderOpen = SalesOrderDocumentStatusEnum.Open,
            OrderClosed = SalesOrderDocumentStatusEnum.Closed,
            Pending = ConfirmationStatusEnum.待确认,
            Confirmed = ConfirmationStatusEnum.已确认,
            DemandActive = GrossDemandLifecycleStatusEnum.Active,
            DemandVoided = GrossDemandLifecycleStatusEnum.Voided,
            NetActive = NetDemandLifecycleStatusEnum.Active,
            LinkActive = DemandExecutionLinkStatusEnum.Active,
            PurchaseVoided = PurchaseOrderLifecycleStatusEnum.Voided,
            Unposted = PackingListPostingStatusEnum.Unposted,
            Posted = PackingListPostingStatusEnum.Posted,
            Reversed = PackingListPostingStatusEnum.Reversed
        }, transaction: Transaction, cancellationToken: cancellationToken);
        using var multi = await Connection.QueryMultipleAsync(command);
        var detail = await multi.ReadSingleOrDefaultAsync<SalesFulfillmentDetailDto>();
        if (detail is null) return null;
        detail.Lines = (await multi.ReadAsync<SalesFulfillmentLineDto>()).ToList();
        var lineById = detail.Lines.ToDictionary(line => line.SalesOrderLineId);
        foreach (var row in await multi.ReadAsync<SalesFulfillmentPurchaseDto>())
            if (lineById.TryGetValue(row.SalesOrderLineId, out var line)) line.Purchases.Add(row);
        foreach (var row in await multi.ReadAsync<SalesFulfillmentDeliveryDto>())
            if (lineById.TryGetValue(row.SalesOrderLineId, out var line)) line.Deliveries.Add(row);
        var invalidLinks = (await multi.ReadAsync<Guid>()).ToHashSet();
        foreach (var line in detail.Lines)
        {
            if (invalidLinks.Contains(line.SalesOrderLineId))
                line.PurchaseReviewReason = "采购关联证据异常";
            line.PurchaseState = line.SourceState == "需核实" ? "未知／需核实"
                : invalidLinks.Contains(line.SalesOrderLineId)
                    || line.Purchases.Any(row => !string.IsNullOrEmpty(row.ReviewReason)) ? "需核实"
                : line.Purchases.Count == 0 ? "未关联" : "已关联";
        }
        return detail;
    }

}
