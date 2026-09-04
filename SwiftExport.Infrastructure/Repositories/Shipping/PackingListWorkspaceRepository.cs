using Dapper;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Models.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

public sealed class PackingListWorkspaceRepository(RepositoryBaseContext context) : IPackingListPlanningRepository, IPackingListQueryRepository
{
    public async Task<PagedResponseBase<PackingListSourceDto>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.PageIndex, 1);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new { request.CustomerId, Keyword = Like(request.Keyword), SalesNo = Like(request.SalesOrderNo), Sku = Like(request.Sku), Supplier = Like(request.SupplierKeyword), Offset = (page - 1) * size, PageSize = size };
        const string where = "WHERE a.LifecycleStatus=0 AND ISNULL(a.IsDeleted,0)=0 AND n.LifecycleStatus=0 AND ISNULL(n.IsDeleted,0)=0 AND g.LifecycleStatus=0 AND ISNULL(g.IsDeleted,0)=0 AND g.SourceType='SO' AND so.CustomerId IS NOT NULL AND n.ProductId IS NOT NULL AND pr.Uid IS NOT NULL AND ISNULL(pr.IsDeleted,0)=0 AND a.PurchaseSupplierId IS NOT NULL AND w.Uid IS NOT NULL AND ISNULL(w.IsActive,0)=1 AND (@CustomerId IS NULL OR so.CustomerId=@CustomerId) AND (@Keyword IS NULL OR so.OrderNo LIKE @Keyword OR n.SkuSnapshot LIKE @Keyword OR a.SupplierNameSnapshot LIKE @Keyword OR a.SupplierCodeSnapshot LIKE @Keyword) AND (@SalesNo IS NULL OR so.OrderNo LIKE @SalesNo) AND (@Sku IS NULL OR n.SkuSnapshot LIKE @Sku) AND (@Supplier IS NULL OR a.SupplierNameSnapshot LIKE @Supplier OR a.SupplierCodeSnapshot LIKE @Supplier)";
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"""
            SELECT COUNT(*)
            FROM dbo.NetDemandAllocation a
            JOIN dbo.NetDemand n ON n.Uid=a.NetDemandId
            JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            JOIN dbo.SalesOrderHeader so ON so.Uid=g.SourceUid
            LEFT JOIN dbo.Product pr ON pr.Uid=n.ProductId
            LEFT JOIN dbo.WarehouseSite w ON w.SupplierId=a.PurchaseSupplierId AND ISNULL(w.IsDeleted,0)=0
            OUTER APPLY(SELECT SUM(l.Quantity) AS ShippedNetQty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
            {where} AND a.PlannedQty-COALESCE(sh.ShippedNetQty,0)>0;
            """, p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<PackingListSourceDto>(new CommandDefinition($"""
            SELECT a.Uid AS NetDemandAllocationId,a.Version AS SourceVersion,n.GrossDemandId,n.Uid AS NetDemandId,n.ProductId,
                   n.SkuSnapshot AS Sku,g.CustomerSkuSnapshot AS CustomerSku,n.DescriptionSnapshot AS DefaultDescriptionEn,COALESCE(NULLIF(LTRIM(RTRIM(pr.Unit)),N''),N'PCS') AS Unit,
                   so.CustomerId,c.CustomerCode,c.CustomerName,a.PlannedQty,COALESCE(sh.ShippedNetQty,0) AS ShippedNetQty,
                   a.PlannedQty-COALESCE(sh.ShippedNetQty,0) AS ShippableRemainingQty,COALESCE(rc.SourceInboundNetQty,0) AS SourceInboundNetQty,
                   COALESCE(progress.ReadyQty,0) AS ReadyQty,so.Uid AS SalesOrderId,g.SourceLineId AS SalesOrderLineId,so.OrderNo AS SalesOrderNo,g.SourceLineNumber AS SalesOrderLineNumber,
                   a.PurchaseSupplierId AS SourceSupplierId,a.SupplierNameSnapshot AS SourceSupplierName,w.Uid AS WarehouseSiteId,w.Code AS WarehouseCode,w.Name AS WarehouseName,
                   n.RequiredDate,a.ExecutionType AS SupplyType,
                   CASE WHEN COALESCE(dl.LinkCount,0)=1 THEN dl.SingleLinkId ELSE NULL END AS DemandExecutionLinkId
            FROM dbo.NetDemandAllocation a JOIN dbo.NetDemand n ON n.Uid=a.NetDemandId JOIN dbo.GrossDemand g ON g.Uid=n.GrossDemandId
            JOIN dbo.SalesOrderHeader so ON so.Uid=g.SourceUid LEFT JOIN dbo.Customer c ON c.Uid=so.CustomerId LEFT JOIN dbo.Product pr ON pr.Uid=n.ProductId
            LEFT JOIN dbo.WarehouseSite w ON w.SupplierId=a.PurchaseSupplierId AND ISNULL(w.IsDeleted,0)=0
            OUTER APPLY(SELECT COUNT(*) AS LinkCount,MAX(Uid) AS SingleLinkId FROM dbo.DemandExecutionLink WHERE NetDemandAllocationId=a.Uid AND LinkStatus=0 AND ISNULL(IsDeleted,0)=0) dl
            OUTER APPLY(SELECT SUM(latest.ReadyQty) AS ReadyQty FROM dbo.DemandExecutionLink link OUTER APPLY(SELECT TOP(1) report.ReadyQty FROM dbo.SupplierProgressReport report WHERE report.PurchaseOrderLineId=link.TargetLineId AND ISNULL(report.IsDeleted,0)=0 ORDER BY report.Sequence DESC,report.Uid DESC) latest WHERE link.NetDemandAllocationId=a.Uid AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0) progress
            OUTER APPLY(SELECT SUM(l.Quantity) AS ShippedNetQty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
            OUTER APPLY(SELECT SUM(l.Quantity) AS SourceInboundNetQty FROM dbo.InboundDocumentLine l JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) rc
            {where} AND a.PlannedQty-COALESCE(sh.ShippedNetQty,0)>0
            ORDER BY so.OrderNo,n.LineNumber,a.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, p, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<PackingListSourceDto> { Items = rows.ToList(), TotalCount = total, PageIndex = page, PageSize = size };
    }

    public Task<PackingListSourceDto?> GetSourceForUpdateAsync(Guid allocationId)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PackingListSourceDto>("""
            SELECT a.Uid AS NetDemandAllocationId,a.Version AS SourceVersion,n.GrossDemandId,n.Uid AS NetDemandId,n.ProductId,
                   n.SkuSnapshot AS Sku,g.CustomerSkuSnapshot AS CustomerSku,n.DescriptionSnapshot AS Description,COALESCE(NULLIF(LTRIM(RTRIM(pr.Unit)),N''),N'PCS') AS Unit,
                   so.CustomerId,c.CustomerCode,c.CustomerName,a.PlannedQty,COALESCE(sh.ShippedNetQty,0) AS ShippedNetQty,
                   a.PlannedQty-COALESCE(sh.ShippedNetQty,0) AS ShippableRemainingQty,COALESCE(rc.SourceInboundNetQty,0) AS SourceInboundNetQty,
                   COALESCE(progress.ReadyQty,0) AS ReadyQty,so.Uid AS SalesOrderId,g.SourceLineId AS SalesOrderLineId,so.OrderNo AS SalesOrderNo,g.SourceLineNumber AS SalesOrderLineNumber,
                   a.PurchaseSupplierId AS SourceSupplierId,a.SupplierNameSnapshot AS SourceSupplierName,w.Uid AS WarehouseSiteId,w.Code AS WarehouseCode,w.Name AS WarehouseName,
                   n.RequiredDate,a.ExecutionType AS SupplyType,CASE WHEN COALESCE(dl.LinkCount,0)=1 THEN dl.SingleLinkId ELSE NULL END AS DemandExecutionLinkId
            FROM dbo.NetDemandAllocation a WITH(UPDLOCK,HOLDLOCK) JOIN dbo.NetDemand n WITH(UPDLOCK,HOLDLOCK) ON n.Uid=a.NetDemandId
            JOIN dbo.GrossDemand g WITH(UPDLOCK,HOLDLOCK) ON g.Uid=n.GrossDemandId JOIN dbo.SalesOrderHeader so ON so.Uid=g.SourceUid
            LEFT JOIN dbo.Customer c ON c.Uid=so.CustomerId LEFT JOIN dbo.Product pr ON pr.Uid=n.ProductId
            LEFT JOIN dbo.WarehouseSite w WITH(UPDLOCK,HOLDLOCK) ON w.SupplierId=a.PurchaseSupplierId AND ISNULL(w.IsDeleted,0)=0
            OUTER APPLY(SELECT COUNT(*) AS LinkCount,MAX(Uid) AS SingleLinkId FROM dbo.DemandExecutionLink WITH(UPDLOCK,HOLDLOCK) WHERE NetDemandAllocationId=a.Uid AND LinkStatus=0 AND ISNULL(IsDeleted,0)=0) dl
            OUTER APPLY(SELECT SUM(latest.ReadyQty) AS ReadyQty FROM dbo.DemandExecutionLink link OUTER APPLY(SELECT TOP(1) report.ReadyQty FROM dbo.SupplierProgressReport report WHERE report.PurchaseOrderLineId=link.TargetLineId AND ISNULL(report.IsDeleted,0)=0 ORDER BY report.Sequence DESC,report.Uid DESC) latest WHERE link.NetDemandAllocationId=a.Uid AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0) progress
            OUTER APPLY(SELECT SUM(l.Quantity) AS ShippedNetQty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
            OUTER APPLY(SELECT SUM(l.Quantity) AS SourceInboundNetQty FROM dbo.InboundDocumentLine l JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) rc
            WHERE a.Uid=@AllocationId AND a.LifecycleStatus=0 AND n.LifecycleStatus=0 AND g.LifecycleStatus=0 AND g.SourceType='SO'
              AND so.CustomerId IS NOT NULL AND pr.Uid IS NOT NULL AND ISNULL(pr.IsDeleted,0)=0 AND w.Uid IS NOT NULL AND ISNULL(w.IsActive,0)=1
              AND ISNULL(a.IsDeleted,0)=0 AND ISNULL(n.IsDeleted,0)=0 AND ISNULL(g.IsDeleted,0)=0;
            """, new { AllocationId = allocationId }, context.Session.Transaction);

    public async Task<PackingListPlanningSnapshot?> LoadSnapshotAsync(Guid uid, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var hint = forUpdate ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
        var document = await context.Session.Connection.QuerySingleOrDefaultAsync<PackingListDocument>(new CommandDefinition($"""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListNo,CustomerId,CustomerCodeSnapshot,CustomerNameSnapshot,
                   PackingDate,ShipmentDate,ETD AS Etd,ETA AS Eta,InvoiceNo,ContainerNo,SealNo,ShippingMark,DeliveryAddressSnapshot,LifecycleStatus,PostingStatus,
                   PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,CreateCommandId,CreateRequestFingerprint,PostCommandId,PostPreviewToken,ReverseCommandId,ReversePreviewToken
            FROM dbo.PackingListDocument{hint} WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));
        if (document is null) return null;
        var lines = (await context.Session.Connection.QueryAsync<PackingListContributionLine>(new CommandDefinition($"""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListDocumentId,LineNumber,ProductId,
                   SkuSnapshot,CustomerSkuSnapshot,DescriptionSnapshot,UnitSnapshot,Quantity,PackageCount,NetWeight,GrossWeight,Volume,Marks,Note,
                   GrossDemandId,NetDemandId,NetDemandAllocationId,DemandExecutionLinkId,SalesOrderId,SalesOrderLineId,SalesOrderNoSnapshot,SalesOrderLineNumberSnapshot,
                   SourceSupplierId,SourceSupplierNameSnapshot,WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,SnapshotRequiredDate,SupplyType,CommandItemId
            FROM dbo.PackingListContributionLine{hint} WHERE PackingListDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY Uid;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();
        var result = new PackingListPlanningSnapshot { Document = document };
        var facts = new List<PackingListPlanningLineSnapshot>();
        foreach (var line in lines.OrderBy(x => x.Uid))
        {
            var row = await context.Session.Connection.QuerySingleOrDefaultAsync<PlanningFactRow>(new CommandDefinition($"""
                SELECT so.CustomerId,a.Version AS SourceVersion,a.PlannedQty,
                       CAST(CASE WHEN a.LifecycleStatus=0 AND n.LifecycleStatus=0 AND g.LifecycleStatus=0 AND g.SourceType='SO' THEN 1 ELSE 0 END AS bit) AS IsSourceActive,
                       CAST(CASE WHEN p.Uid IS NOT NULL AND ISNULL(p.IsDeleted,0)=0 THEN 1 ELSE 0 END AS bit) AS IsProductActive,
                       CAST(CASE WHEN w.Uid IS NOT NULL AND ISNULL(w.IsActive,0)=1 THEN 1 ELSE 0 END AS bit) AS IsWarehouseActive,
                       COALESCE(sh.Qty,0) AS ShippedNetQty,COALESCE(rc.Qty,0) AS SourceInboundNetQty,COALESCE(progress.ReadyQty,0) AS ReadyQty,
                       COALESCE(i.OnHandQty,0) AS BalanceQty,COALESCE(i.Version,0) AS BalanceVersion
                FROM dbo.NetDemandAllocation a{hint} JOIN dbo.NetDemand n{hint} ON n.Uid=a.NetDemandId
                JOIN dbo.GrossDemand g{hint} ON g.Uid=n.GrossDemandId LEFT JOIN dbo.SalesOrderHeader so ON so.Uid=g.SourceUid
                LEFT JOIN dbo.Product p ON p.Uid=@ProductId LEFT JOIN dbo.WarehouseSite w{hint} ON w.Uid=@WarehouseSiteId
                LEFT JOIN dbo.InventoryBalance i{hint} ON i.WarehouseSiteId=@WarehouseSiteId AND i.ProductId=@ProductId AND ISNULL(i.IsDeleted,0)=0
                OUTER APPLY(SELECT SUM(l.Quantity) Qty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
                OUTER APPLY(SELECT SUM(l.Quantity) Qty FROM dbo.InboundDocumentLine l JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId WHERE l.NetDemandAllocationId=a.Uid AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) rc
                OUTER APPLY(SELECT SUM(latest.ReadyQty) ReadyQty FROM dbo.DemandExecutionLink link OUTER APPLY(SELECT TOP(1) report.ReadyQty FROM dbo.SupplierProgressReport report WHERE report.PurchaseOrderLineId=link.TargetLineId AND ISNULL(report.IsDeleted,0)=0 ORDER BY report.Sequence DESC,report.Uid DESC) latest WHERE link.NetDemandAllocationId=a.Uid AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0) progress
                WHERE a.Uid=@AllocationId;
                """, new { AllocationId = line.NetDemandAllocationId, line.WarehouseSiteId, line.ProductId }, context.Session.Transaction, cancellationToken: cancellationToken)) ?? new PlanningFactRow();
            var children = (await context.Session.Connection.QueryAsync<PackingListCustomerSuppliedSnapshot>(new CommandDefinition($"""
                SELECT b.Uid AS AllocationId,bn.ProductId,bn.SkuSnapshot AS Sku,COALESCE(NULLIF(LTRIM(RTRIM(bp.Unit)),N''),N'PCS') AS Unit,b.PlannedQty,a.PlannedQty AS ParentPlannedQty,
                       COALESCE(cons.Qty,0) AS HistoricalConsumptionQty,COALESCE(inv.OnHandQty,0) AS BalanceQty,COALESCE(inv.Version,0) AS BalanceVersion
                FROM dbo.NetDemandAllocation b{hint} JOIN dbo.NetDemand bn{hint} ON bn.Uid=b.NetDemandId JOIN dbo.NetDemandAllocation a{hint} ON a.Uid=b.DeliveryTargetAllocationId
                LEFT JOIN dbo.Product bp ON bp.Uid=bn.ProductId LEFT JOIN dbo.InventoryBalance inv{hint} ON inv.WarehouseSiteId=@WarehouseSiteId AND inv.ProductId=bn.ProductId AND ISNULL(inv.IsDeleted,0)=0
                OUTER APPLY(SELECT SUM(ol.Quantity) Qty FROM dbo.OutboundDocumentLine ol JOIN dbo.OutboundDocument oh ON oh.Uid=ol.OutboundDocumentId WHERE ol.NetDemandAllocationId=b.Uid AND oh.DocumentType=21 AND oh.PostingStatus=1 AND ISNULL(ol.IsDeleted,0)=0 AND ISNULL(oh.IsDeleted,0)=0) cons
                WHERE b.DeliveryTargetAllocationId=@AllocationId AND b.LifecycleStatus=0 AND ISNULL(b.IsDeleted,0)=0 AND bn.LifecycleStatus=0 AND ISNULL(bn.IsDeleted,0)=0;
                """, new { AllocationId = line.NetDemandAllocationId, line.WarehouseSiteId }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();
            facts.Add(new PackingListPlanningLineSnapshot
            {
                Line = line, CustomerId = row.CustomerId, SourceVersion = row.SourceVersion, PlannedQty = row.PlannedQty,
                IsSourceActive = row.IsSourceActive, IsProductActive = row.IsProductActive, IsWarehouseActive = row.IsWarehouseActive,
                ShippedNetQty = row.ShippedNetQty, SourceInboundNetQty = row.SourceInboundNetQty, ReadyQty = row.ReadyQty,
                BalanceQty = row.BalanceQty, BalanceVersion = row.BalanceVersion, CustomerSuppliedItems = children
            });
        }
        result.Lines = facts;
        return result;
    }

    public async Task<PagedResponseBase<PackingListListItemDto>> SearchAsync(PackingListSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.PageIndex, 1); var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new { Keyword = Like(request.Keyword), No = Like(request.PackingListNo), Invoice = Like(request.InvoiceNo), Customer = Like(request.CustomerKeyword),
            Sales = Like(request.SalesOrderNo), Supplier = Like(request.SupplierKeyword), Sku = Like(request.Sku), request.PostingStatus, request.ShipmentDateFrom, request.ShipmentDateTo,
            Offset = (page - 1) * size, PageSize = size };
        const string where = "WHERE ISNULL(h.IsDeleted,0)=0 AND (@Keyword IS NULL OR h.PackingListNo LIKE @Keyword OR h.InvoiceNo LIKE @Keyword OR h.CustomerNameSnapshot LIKE @Keyword) AND (@No IS NULL OR h.PackingListNo LIKE @No) AND (@Invoice IS NULL OR h.InvoiceNo LIKE @Invoice) AND (@Customer IS NULL OR h.CustomerCodeSnapshot LIKE @Customer OR h.CustomerNameSnapshot LIKE @Customer) AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus) AND (@ShipmentDateFrom IS NULL OR h.ShipmentDate>=@ShipmentDateFrom) AND (@ShipmentDateTo IS NULL OR h.ShipmentDate<DATEADD(day,1,@ShipmentDateTo)) AND (@Sales IS NULL OR EXISTS(SELECT 1 FROM dbo.PackingListContributionLine x WHERE x.PackingListDocumentId=h.Uid AND x.SalesOrderNoSnapshot LIKE @Sales AND ISNULL(x.IsDeleted,0)=0)) AND (@Supplier IS NULL OR EXISTS(SELECT 1 FROM dbo.PackingListContributionLine x WHERE x.PackingListDocumentId=h.Uid AND x.SourceSupplierNameSnapshot LIKE @Supplier AND ISNULL(x.IsDeleted,0)=0)) AND (@Sku IS NULL OR EXISTS(SELECT 1 FROM dbo.PackingListContributionLine x WHERE x.PackingListDocumentId=h.Uid AND x.SkuSnapshot LIKE @Sku AND ISNULL(x.IsDeleted,0)=0))";
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.PackingListDocument h {where};", p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<PackingListListItemDto>(new CommandDefinition($"""
            SELECT COALESCE(h.Uid,'00000000-0000-0000-0000-000000000000') AS Uid,COALESCE(h.Version,0) AS Version,h.PackingListNo,h.CustomerId,h.CustomerCodeSnapshot AS CustomerCode,h.CustomerNameSnapshot AS CustomerName,h.InvoiceNo,h.ShipmentDate,
                   h.LifecycleStatus,h.PostingStatus,COUNT(l.Uid) AS LineCount,COALESCE(SUM(l.Quantity),0) AS TotalQuantity,
                   CAST(0 AS bit) AS IsLegacyReadOnly,CAST(NULL AS nvarchar(500)) AS ReadOnlyReason
            FROM dbo.PackingListDocument h LEFT JOIN dbo.PackingListContributionLine l ON l.PackingListDocumentId=h.Uid AND ISNULL(l.IsDeleted,0)=0 {where}
            GROUP BY h.Uid,h.Version,h.PackingListNo,h.CustomerId,h.CustomerCodeSnapshot,h.CustomerNameSnapshot,h.InvoiceNo,h.ShipmentDate,h.LifecycleStatus,h.PostingStatus
            ORDER BY h.ShipmentDate DESC,h.PackingListNo DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, p, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<PackingListListItemDto> { Items = rows.ToList(), TotalCount = total, PageIndex = page, PageSize = size };
    }

    public async Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        using var multi = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT Uid,Version,PackingListNo,CustomerId,CustomerCodeSnapshot AS CustomerCode,CustomerNameSnapshot AS CustomerName,InvoiceNo,ContainerNo,SealNo,ShippingMark,
                   DeliveryAddressSnapshot,PackingDate,ShipmentDate,Etd,Eta,LifecycleStatus,PostingStatus,PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,
                   CAST(0 AS bit) AS IsLegacyReadOnly,CAST(NULL AS nvarchar(500)) AS ReadOnlyReason
            FROM dbo.PackingListDocument WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
            SELECT l.Uid,l.Version,l.LineNumber,l.ProductId,l.SkuSnapshot AS Sku,l.CustomerSkuSnapshot AS CustomerSku,l.DescriptionSnapshot AS Description,l.UnitSnapshot AS Unit,l.Quantity,l.PackageCount,l.NetWeight,l.GrossWeight,l.Volume,
                   l.Marks,l.Note,l.GrossDemandId,l.NetDemandId,l.NetDemandAllocationId,a.Version AS SourceVersion,a.PlannedQty-COALESCE(sh.Qty,0) AS ShippableRemainingQty,l.DemandExecutionLinkId,l.SalesOrderId,l.SalesOrderLineId,l.SalesOrderNoSnapshot AS SalesOrderNo,
                   SalesOrderLineNumberSnapshot AS SalesOrderLineNumber,SourceSupplierId,SourceSupplierNameSnapshot AS SourceSupplierName,WarehouseSiteId,WarehouseCodeSnapshot AS WarehouseCode,
                   WarehouseNameSnapshot AS WarehouseName,SnapshotRequiredDate AS RequiredDate,SupplyType,CommandItemId
            FROM dbo.PackingListContributionLine l LEFT JOIN dbo.NetDemandAllocation a ON a.Uid=l.NetDemandAllocationId
            OUTER APPLY(SELECT SUM(ol.Quantity) Qty FROM dbo.OutboundDocumentLine ol JOIN dbo.OutboundDocument oh ON oh.Uid=ol.OutboundDocumentId WHERE ol.NetDemandAllocationId=l.NetDemandAllocationId AND oh.DocumentType=20 AND oh.PostingStatus=1 AND ISNULL(ol.IsDeleted,0)=0 AND ISNULL(oh.IsDeleted,0)=0) sh
            WHERE l.PackingListDocumentId=@Uid AND ISNULL(l.IsDeleted,0)=0 ORDER BY l.LineNumber;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));
        var header = await multi.ReadSingleOrDefaultAsync<PackingListHeaderDto>();
        if (header is null) return null;
        return new PackingListDetailDto { Header = header, Lines = (await multi.ReadAsync<PackingListContributionDto>()).ToList() };
    }

    public async Task<PackingListTraceDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var detail = await GetDetailAsync(uid, cancellationToken); if (detail is null) return null;
        var trace = new PackingListTraceDto { Document = detail };
        foreach (var line in detail.Lines)
        {
            trace.Items.Add(new PackingListTraceItemDto { Stage = "SalesOrder", Uid = line.SalesOrderId, No = line.SalesOrderNo, Description = $"销售行 {line.SalesOrderLineNumber}", Quantity = line.Quantity });
            trace.Items.Add(new PackingListTraceItemDto { Stage = "GrossDemand", Uid = line.GrossDemandId, No = line.SalesOrderNo, Description = line.Sku, Quantity = line.Quantity });
            trace.Items.Add(new PackingListTraceItemDto { Stage = "Allocation", Uid = line.NetDemandAllocationId, No = line.SourceSupplierName, Description = line.WarehouseName, Quantity = line.Quantity });
        }
        // Allocation 是来源权威：追溯时实时返回其全部活动 Link，不依赖行上仅在单 Link 时保存的快照。
        var executionLinks = await context.Session.Connection.QueryAsync<PackingListTraceItemDto>(new CommandDefinition("""
            SELECT N'DemandExecutionLink' AS Stage,link.Uid,link.TargetNo AS No,
                   CONCAT(link.TargetType,N' / 目标行 ',CONVERT(nvarchar(36),link.TargetLineId)) AS Description,
                   link.AllocatedQty AS Quantity
            FROM dbo.PackingListContributionLine pl
            JOIN dbo.DemandExecutionLink link ON link.NetDemandAllocationId=pl.NetDemandAllocationId
            WHERE pl.PackingListDocumentId=@Uid AND ISNULL(pl.IsDeleted,0)=0
              AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0
            ORDER BY pl.LineNumber,link.TargetType,link.TargetNo,link.TargetLineId;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));
        trace.Items.AddRange(executionLinks);
        var generated = await context.Session.Connection.QueryAsync<PackingListTraceItemDto>(new CommandDefinition("""
            SELECT N'Inbound' AS Stage,Uid,DocumentNo AS No,WarehouseNameSnapshot AS Description,CAST(NULL AS decimal(18,4)) AS Quantity FROM dbo.InboundDocument WHERE SourcePackingListId=@Uid AND ISNULL(IsDeleted,0)=0
            UNION ALL SELECT N'Outbound',Uid,DocumentNo,WarehouseNameSnapshot,NULL FROM dbo.OutboundDocument WHERE SourcePackingListId=@Uid AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));
        trace.Items.AddRange(generated); return trace;
    }

    private sealed class PlanningFactRow
    {
        public Guid CustomerId { get; set; }
        public int SourceVersion { get; set; }
        public decimal PlannedQty { get; set; }
        public bool IsSourceActive { get; set; }
        public bool IsProductActive { get; set; }
        public bool IsWarehouseActive { get; set; }
        public decimal ShippedNetQty { get; set; }
        public decimal SourceInboundNetQty { get; set; }
        public decimal ReadyQty { get; set; }
        public decimal BalanceQty { get; set; }
        public int BalanceVersion { get; set; }
    }

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
}
