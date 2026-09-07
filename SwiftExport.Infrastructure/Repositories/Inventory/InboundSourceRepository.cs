using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>入库来源只读/锁定查询；数量全部由有效入库事实实时汇总。</summary>
public sealed class InboundSourceRepository(RepositoryBaseContext context) : IInboundSourceRepository
{
    public async Task<PagedResponseBase<InboundSourceDto>> SearchAsync(InboundSourceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex); var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var all = await LoadAsync(request.DocumentType, false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            all = all.Where(x => (x.Sku ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) || (x.PurchaseOrderNo ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) || (x.SalesOrderNo ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) || (x.SupplierName ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        all = all.Where(x => x.RemainingReceivableQty > 0).OrderBy(x => x.PurchaseOrderNo).ThenBy(x => x.Sku).ToList();
        return new PagedResponseBase<InboundSourceDto> { Items = all.Skip((index - 1) * size).Take(size).ToList(), TotalCount = all.Count, PageIndex = index, PageSize = size };
    }

    public async Task<InboundSourceDto?> GetForUpdateAsync(InboundSourceTypeEnum sourceType, Guid sourceUid)
    {
        var type = sourceType == InboundSourceTypeEnum.NetDemandAllocation ? InboundDocumentTypeEnum.CustomerSuppliedReceipt : InboundDocumentTypeEnum.PurchaseReceipt;
        return (await LoadAsync(type, true, default)).SingleOrDefault(x => x.SourceUid == sourceUid && x.SourceType == sourceType);
    }

    public Task<decimal> GetReceivedNetQtyAsync(InboundSourceTypeEnum sourceType, Guid sourceUid, Guid? excludeInboundDocumentId = null) => context.Session.Connection.ExecuteScalarAsync<decimal>("""
        SELECT ISNULL(SUM(CASE WHEN h.PostingStatus=@Posted THEN l.Quantity ELSE 0 END),0)
        FROM dbo.InboundDocumentLine l INNER JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId
        WHERE l.SourceType=@SourceType AND l.SourceUid=@SourceUid AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0
          AND (@ExcludeInboundDocumentId IS NULL OR h.Uid<>@ExcludeInboundDocumentId);
        """, new { SourceType = sourceType, SourceUid = sourceUid, ExcludeInboundDocumentId = excludeInboundDocumentId, Posted = InboundPostingStatusEnum.Posted }, context.Session.Transaction);

    public Task<bool> HasDownstreamOutboundAsync(Guid inboundDocumentId) => context.Session.Connection.ExecuteScalarAsync<bool>("""
        SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry t INNER JOIN dbo.InboundDocumentLine l ON l.Uid=t.SourceLineId WHERE l.InboundDocumentId=@InboundDocumentId AND t.TransactionType IN (20,21)) THEN 1 ELSE 0 END AS bit);
        """, new { InboundDocumentId = inboundDocumentId }, context.Session.Transaction);

    private async Task<List<InboundSourceDto>> LoadAsync(InboundDocumentTypeEnum type, bool lockRows, CancellationToken cancellationToken)
    {
        var hint = lockRows ? "WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
        var sql = type == InboundDocumentTypeEnum.CustomerSuppliedReceipt ? $"""
            SELECT a.Uid AS SourceUid,a.Version AS SourceVersion,CAST(@SourceType AS int) AS SourceType,nd.ProductId,nd.SkuSnapshot AS Sku,COALESCE(p.DescriptionZh,p.DescriptionEn,nd.SkuSnapshot) AS DefaultDescriptionEn,COALESCE(NULLIF(LTRIM(RTRIM(p.Unit)),N''),N'PCS') AS Unit,
                   a.PlannedQty AS EffectiveQty,CAST(0 AS decimal(18,4)) AS ReceivedQty,a.PlannedQty AS RemainingReceivableQty,w.Uid AS WarehouseSiteId,w.Name AS WarehouseName,w.SupplierId,w.SupplierNameSnapshot AS SupplierName,
                   CAST(NULL AS uniqueidentifier) AS DemandExecutionLinkId,a.Uid AS NetDemandAllocationId,CAST(NULL AS nvarchar(100)) AS PurchaseOrderNo,gd.SourceNo AS SalesOrderNo
            FROM dbo.NetDemandAllocation a {hint} INNER JOIN dbo.NetDemand nd ON nd.Uid=a.NetDemandId AND ISNULL(nd.IsDeleted,0)=0 INNER JOIN dbo.GrossDemand gd ON gd.Uid=nd.GrossDemandId AND ISNULL(gd.IsDeleted,0)=0
            INNER JOIN dbo.NetDemandAllocation parent ON parent.Uid=a.DeliveryTargetAllocationId AND parent.LifecycleStatus=0 AND ISNULL(parent.IsDeleted,0)=0
            INNER JOIN dbo.WarehouseSite w ON w.SupplierId=parent.PurchaseSupplierId AND w.SiteType=0 AND w.IsActive=1 AND ISNULL(w.IsDeleted,0)=0 LEFT JOIN dbo.Product p ON p.Uid=nd.ProductId AND ISNULL(p.IsDeleted,0)=0
            WHERE a.LifecycleStatus=0 AND a.DeliveryTargetAllocationId IS NOT NULL AND ISNULL(a.IsDeleted,0)=0;
            """ : $"""
            SELECT l.Uid AS SourceUid,l.Version AS SourceVersion,CAST(@SourceType AS int) AS SourceType,pol.ProductId,pol.Sku,COALESCE(p.DescriptionZh,p.DescriptionEn,pol.Sku) AS DefaultDescriptionEn,COALESCE(NULLIF(LTRIM(RTRIM(pol.UnitSnapshot)),N''),NULLIF(LTRIM(RTRIM(p.Unit)),N''),N'PCS') AS Unit,
                   l.AllocatedQty-l.ReleasedQty AS EffectiveQty,CAST(0 AS decimal(18,4)) AS ReceivedQty,l.AllocatedQty-l.ReleasedQty AS RemainingReceivableQty,w.Uid AS WarehouseSiteId,w.Name AS WarehouseName,w.SupplierId,w.SupplierNameSnapshot AS SupplierName,
                   l.Uid AS DemandExecutionLinkId,l.NetDemandAllocationId,h.OrderNo AS PurchaseOrderNo,gd.SourceNo AS SalesOrderNo
            FROM dbo.DemandExecutionLink l {hint} INNER JOIN dbo.PurchaseOrderLine pol ON pol.Uid=l.TargetLineId AND ISNULL(pol.IsDeleted,0)=0 INNER JOIN dbo.PurchaseOrderHead h ON h.Uid=l.TargetHeadId AND h.ConfirmationStatus=1 AND ISNULL(h.IsDeleted,0)=0
            INNER JOIN dbo.NetDemandAllocation a ON a.Uid=l.NetDemandAllocationId AND a.LifecycleStatus=0 AND ISNULL(a.IsDeleted,0)=0 INNER JOIN dbo.NetDemand nd ON nd.Uid=l.NetDemandId AND ISNULL(nd.IsDeleted,0)=0 INNER JOIN dbo.GrossDemand gd ON gd.Uid=nd.GrossDemandId AND ISNULL(gd.IsDeleted,0)=0
            INNER JOIN dbo.WarehouseSite w ON w.SupplierId=COALESCE(a.DeliveryTargetSupplierId,h.SupplierId) AND w.SiteType=0 AND w.IsActive=1 AND ISNULL(w.IsDeleted,0)=0 LEFT JOIN dbo.Product p ON p.Uid=pol.ProductId AND ISNULL(p.IsDeleted,0)=0
            WHERE l.TargetType=N'PO' AND l.LinkStatus=0 AND ISNULL(l.IsDeleted,0)=0;
            """;
        var sourceType = type == InboundDocumentTypeEnum.CustomerSuppliedReceipt ? InboundSourceTypeEnum.NetDemandAllocation : InboundSourceTypeEnum.PurchaseOrderLine;
        var rows = (await context.Session.Connection.QueryAsync<InboundSourceDto>(new CommandDefinition(sql, new { SourceType = sourceType }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();
        foreach (var row in rows) { row.ReceivedQty = await GetReceivedNetQtyAsync(row.SourceType, row.SourceUid); row.RemainingReceivableQty = row.EffectiveQty - row.ReceivedQty; }
        return rows;
    }
}
