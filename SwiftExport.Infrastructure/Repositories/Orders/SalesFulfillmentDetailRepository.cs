using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Interfaces.Orders;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>按销售订单读取销售行、来源需求、行级采购组件和订单级采购关联。</summary>
public sealed class SalesFulfillmentDetailRepository(RepositoryBaseContext context) : ISalesFulfillmentDetailRepository
{
    /// <summary>以销售行为主集合读取履约详情，并在各事实聚合完成后再关联，避免多 Link 和多跟进造成乘法重复。</summary>
    public async Task<SalesFulfillmentDetailDto?> GetAsync(Guid salesOrderId, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT h.Uid AS SalesOrderId,h.Version AS SalesOrderVersion,h.OrderNo,h.CustomerCode,
       ISNULL(c.CustomerName,N'') AS CustomerName,h.DeliveryDate,
       (SELECT MAX(d.SourceVersion) FROM dbo.GrossDemand d WITH(NOLOCK)
        WHERE d.SourceType=@SourceType AND d.SourceUid=h.Uid AND d.IsDeleted=0) AS LatestSourceVersion
FROM dbo.SalesOrderHeader h WITH(NOLOCK)
LEFT JOIN dbo.Customer c WITH(NOLOCK) ON c.Uid=h.CustomerId AND c.IsDeleted=0
WHERE h.Uid=@SalesOrderId AND h.IsDeleted=0;

SELECT l.Uid AS SalesOrderLineId,l.LineNumber,l.Sku,l.CustomerSku,l.DescriptionEn AS Description,
       l.Qty AS DemandQty,COALESCE(l.ExpectedDeliveryDate,h.DeliveryDate) AS RequiredDate,
       chosen.Uid AS SourceDemandId,chosen.SourceVersion,chosen.LifecycleStatus AS SourceLifecycleStatus,
       CASE WHEN chosen.Uid IS NULL OR counts.ActiveCount>1 THEN NULL ELSE ISNULL(shipped.ShippedQty,0) END AS ShippedQty,
       CASE WHEN chosen.Uid IS NULL OR counts.ActiveCount>1 THEN NULL
            WHEN l.Qty>ISNULL(shipped.ShippedQty,0) THEN l.Qty-ISNULL(shipped.ShippedQty,0) ELSE 0 END AS UnshippedQty,
       CASE WHEN counts.ActiveCount>1 THEN N'DUPLICATE_ACTIVE_SOURCE'
            WHEN chosen.Uid IS NULL THEN N'SOURCE_DEMAND_MISSING' ELSE N'' END AS AnomalyCode,
       CASE WHEN counts.ActiveCount>1 THEN N'同一销售行存在多条活动来源需求。'
            WHEN chosen.Uid IS NULL THEN N'销售行尚未建立来源需求，已发与未发数量未知。' ELSE N'' END AS AnomalyMessage
FROM dbo.SalesOrderLine l WITH(NOLOCK)
JOIN dbo.SalesOrderHeader h WITH(NOLOCK) ON h.Uid=l.SalesOrderId AND h.IsDeleted=0
OUTER APPLY(
    SELECT COUNT(CASE WHEN d.LifecycleStatus=0 THEN 1 END) AS ActiveCount
    FROM dbo.GrossDemand d WITH(NOLOCK)
    WHERE d.SourceType=@SourceType AND d.SourceUid=h.Uid AND d.SourceLineId=l.Uid AND d.IsDeleted=0
) counts
OUTER APPLY(
    SELECT TOP(1) d.Uid,d.SourceVersion,d.LifecycleStatus
    FROM dbo.GrossDemand d WITH(NOLOCK)
    WHERE d.SourceType=@SourceType AND d.SourceUid=h.Uid AND d.SourceLineId=l.Uid AND d.IsDeleted=0
    ORDER BY CASE WHEN d.LifecycleStatus=0 THEN 0 ELSE 1 END,d.SourceVersion DESC,d.CreatedDate DESC,d.Uid DESC
) chosen
OUTER APPLY(
    SELECT SUM(ol.Quantity) AS ShippedQty
    FROM dbo.OutboundDocumentLine ol WITH(NOLOCK)
    JOIN dbo.OutboundDocument oh WITH(NOLOCK) ON oh.Uid=ol.OutboundDocumentId
    JOIN dbo.NetDemandAllocation a WITH(NOLOCK) ON a.Uid=ol.NetDemandAllocationId
    JOIN dbo.NetDemand nd WITH(NOLOCK) ON nd.Uid=a.NetDemandId
    JOIN dbo.GrossDemand d WITH(NOLOCK) ON d.Uid=nd.GrossDemandId
    WHERE d.SourceType=@SourceType AND d.SourceUid=h.Uid AND d.SourceLineId=l.Uid
      AND oh.DocumentType=20 AND oh.PostingStatus=1 AND oh.IsDeleted=0 AND ol.IsDeleted=0
) shipped
WHERE l.SalesOrderId=@SalesOrderId AND l.IsDeleted=0
ORDER BY l.LineNumber,l.Uid;

WITH LinkFacts AS
(
    SELECT d.SourceLineId,link.TargetHeadId AS PurchaseOrderId,link.TargetLineId AS PurchaseOrderLineId,
           SUM(link.AllocatedQty-link.ReleasedQty) AS AllocatedQty,
           SUM(ISNULL(receipt.ReceivedQty,0)) AS ReceivedQty
    FROM dbo.GrossDemand d WITH(NOLOCK)
    JOIN dbo.NetDemand nd WITH(NOLOCK) ON nd.GrossDemandId=d.Uid AND nd.IsDeleted=0
    JOIN dbo.DemandExecutionLink link WITH(NOLOCK) ON link.NetDemandId=nd.Uid
         AND link.TargetType=N'PO' AND link.LinkStatus=0 AND link.IsDeleted=0
    OUTER APPLY(
        SELECT SUM(il.Quantity) AS ReceivedQty
        FROM dbo.InboundDocumentLine il WITH(NOLOCK)
        JOIN dbo.InboundDocument ih WITH(NOLOCK) ON ih.Uid=il.InboundDocumentId
        WHERE il.DemandExecutionLinkId=link.Uid AND il.IsDeleted=0 AND ih.IsDeleted=0 AND ih.PostingStatus=1
    ) receipt
    WHERE d.SourceType=@SourceType AND d.SourceUid=@SalesOrderId AND d.IsDeleted=0
    GROUP BY d.SourceLineId,link.TargetHeadId,link.TargetLineId
)
SELECT poh.Uid AS PurchaseOrderId,poh.OrderNo AS PurchaseOrderNo,pol.Uid AS PurchaseOrderLineId,
       pol.LineNumber AS PurchaseOrderLineNumber,f.SourceLineId AS SalesOrderLineId,pol.Sku,
       pol.UnitSnapshot AS Unit,poh.SupplierCode,ISNULL(poh.SupplierName,N'') AS SupplierName,
       poh.ConfirmationStatus,f.AllocatedQty,f.ReceivedQty,latest.ReadyQty AS LatestReadyQty,
       latest.ExpectedReadyDate,latest.SupplierReportedDate AS LatestFollowUpDate,CAST(0 AS bit) AS IsOrderLevelOnly
FROM LinkFacts f
JOIN dbo.PurchaseOrderHead poh WITH(NOLOCK) ON poh.Uid=f.PurchaseOrderId AND poh.IsDeleted=0
JOIN dbo.PurchaseOrderLine pol WITH(NOLOCK) ON pol.Uid=f.PurchaseOrderLineId AND pol.PurchaseOrderId=poh.Uid AND pol.IsDeleted=0
OUTER APPLY(
    SELECT TOP(1) report.ReadyQty,report.ExpectedReadyDate,report.SupplierReportedDate
    FROM dbo.SupplierProgressReport report WITH(NOLOCK)
    WHERE report.PurchaseOrderLineId=pol.Uid AND report.IsDeleted=0
    ORDER BY report.Sequence DESC,report.Uid DESC
) latest
ORDER BY f.SourceLineId,poh.OrderNo,pol.LineNumber,pol.Uid;

SELECT poh.Uid AS PurchaseOrderId,poh.OrderNo AS PurchaseOrderNo,poh.SupplierCode,
       ISNULL(poh.SupplierName,N'') AS SupplierName,poh.ConfirmationStatus,
       CAST(1 AS bit) AS IsOrderLevelOnly
FROM dbo.PurchaseOrderHead poh WITH(NOLOCK)
WHERE poh.DemandSourceType=@SourceType AND poh.DemandSourceUid=@SalesOrderId AND poh.IsDeleted=0
  AND NOT EXISTS(
      SELECT 1
      FROM dbo.DemandExecutionLink link WITH(NOLOCK)
      JOIN dbo.NetDemand nd WITH(NOLOCK) ON nd.Uid=link.NetDemandId AND nd.IsDeleted=0
      JOIN dbo.GrossDemand d WITH(NOLOCK) ON d.Uid=nd.GrossDemandId AND d.IsDeleted=0
      WHERE link.TargetType=N'PO' AND link.TargetHeadId=poh.Uid AND link.IsDeleted=0
        AND d.SourceType=@SourceType AND d.SourceUid=@SalesOrderId
  )
ORDER BY poh.OrderNo,poh.Uid;
""";
        using var multi = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            new { SalesOrderId = salesOrderId, SourceType = DocumentTypeStaticsEnum.SalesOrder },
            context.Session.Transaction,
            cancellationToken: cancellationToken));
        var detail = await multi.ReadSingleOrDefaultAsync<SalesFulfillmentDetailDto>();
        if (detail is null) return null;
        detail.Lines = (await multi.ReadAsync<SalesFulfillmentDetailLineDto>()).ToList();
        detail.LinePurchases = (await multi.ReadAsync<SalesFulfillmentPurchaseDto>()).ToList();
        detail.OrderPurchases = (await multi.ReadAsync<SalesFulfillmentPurchaseDto>()).ToList();
        return detail;
    }
}
