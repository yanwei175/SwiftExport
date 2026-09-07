using Dapper;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Models.Shipping;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>提供 PL 来源需求计划投影、提交事实快照和单据只读查询。</summary>
/// <summary>提供 PL 来源需求计划投影、提交事实快照和单据只读查询。</summary>
/// <summary>提供 PL 来源需求计划投影、提交事实快照和单据只读查询。</summary>
public sealed class PackingListWorkspaceRepository(RepositoryBaseContext context) : IPackingListPlanningRepository, IPackingListQueryRepository
{
    public async Task<PagedResponseBase<PackingListSourceDto>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.PageIndex, 1);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new { request.CustomerId, Keyword = Like(request.Keyword), SalesNo = Like(request.SalesOrderNo), Sku = Like(request.Sku), Supplier = Like(request.SupplierKeyword), Offset = (page - 1) * size, PageSize = size };
        const string where = "WHERE g.LifecycleStatus=0 AND ISNULL(g.IsDeleted,0)=0 AND g.SourceType IN('SO','CR') AND g.CustomerId IS NOT NULL AND g.ProductId IS NOT NULL AND pr.Uid IS NOT NULL AND ISNULL(pr.IsDeleted,0)=0 AND (@CustomerId IS NULL OR g.CustomerId=@CustomerId) AND (@Keyword IS NULL OR g.SourceNo LIKE @Keyword OR g.SkuSnapshot LIKE @Keyword OR planHint.SupplierNameSnapshot LIKE @Keyword) AND (@SalesNo IS NULL OR g.SourceNo LIKE @SalesNo) AND (@Sku IS NULL OR g.SkuSnapshot LIKE @Sku) AND (@Supplier IS NULL OR planHint.SupplierNameSnapshot LIKE @Supplier OR planHint.SupplierCodeSnapshot LIKE @Supplier)";
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"""
            SELECT COUNT(*)
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.Product pr ON pr.Uid=g.ProductId
            OUTER APPLY({PlanningExecutionApply}) planHint
            OUTER APPLY(SELECT SUM(l.Quantity) AS ShippedNetQty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.GrossDemandId=g.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
            {where} AND g.DemandQty-COALESCE(sh.ShippedNetQty,0)>0;
            """, p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<PackingListSourceDto>(new CommandDefinition($"""
            SELECT g.Uid AS GrossDemandId,g.Version AS DemandVersion,g.SourceType,g.SourceUid,g.SourceLineId,g.SourceVersion,
                   planHint.NetDemandAllocationId,planHint.NetDemandId,g.ProductId,g.SkuSnapshot AS Sku,g.CustomerSkuSnapshot AS CustomerSku,g.DescriptionSnapshot AS Description,
                   COALESCE(NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N''),NULLIF(LTRIM(RTRIM(pr.Unit)),N'')) AS Unit,
                   g.CustomerId,c.CustomerCode,c.CustomerName,g.DemandQty AS PlannedQty,COALESCE(sh.ShippedNetQty,0) AS ShippedNetQty,
                   g.DemandQty-COALESCE(sh.ShippedNetQty,0) AS ShippableRemainingQty,COALESCE(rc.SourceInboundNetQty,0) AS SourceInboundNetQty,
                   COALESCE(progress.ReadyQty,0) AS ReadyQty,CASE WHEN g.SourceType='SO' THEN g.SourceUid ELSE NULL END AS SalesOrderId,
                   CASE WHEN g.SourceType='SO' THEN g.SourceLineId ELSE NULL END AS SalesOrderLineId,g.SourceNo AS SalesOrderNo,g.SourceLineNumber AS SalesOrderLineNumber,
                   planHint.PurchaseSupplierId AS SourceSupplierId,planHint.SupplierNameSnapshot AS SourceSupplierName,planHint.WarehouseSiteId,planHint.WarehouseCode,planHint.WarehouseName,
                   g.RequiredDate,COALESCE(planHint.ExecutionType,0) AS SupplyType,planHint.DemandExecutionLinkId
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.Customer c ON c.Uid=g.CustomerId LEFT JOIN dbo.Product pr ON pr.Uid=g.ProductId
            OUTER APPLY({PlanningExecutionApply}) planHint
            OUTER APPLY(SELECT SUM(l.Quantity) AS ShippedNetQty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.GrossDemandId=g.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
            OUTER APPLY(SELECT SUM(l.Quantity) AS SourceInboundNetQty FROM dbo.InboundDocumentLine l JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId WHERE l.NetDemandAllocationId=planHint.NetDemandAllocationId AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) rc
            OUTER APPLY(SELECT SUM(latest.ReadyQty) AS ReadyQty FROM dbo.DemandExecutionLink link OUTER APPLY(SELECT TOP(1) report.ReadyQty FROM dbo.SupplierProgressReport report WHERE report.PurchaseOrderLineId=link.TargetLineId AND ISNULL(report.IsDeleted,0)=0 ORDER BY report.Sequence DESC,report.Uid DESC) latest WHERE link.NetDemandAllocationId=planHint.NetDemandAllocationId AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0) progress
            {where} AND g.DemandQty-COALESCE(sh.ShippedNetQty,0)>0
            ORDER BY g.RequiredDate,g.SourceNo,g.SourceLineNumber,g.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, p, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<PackingListSourceDto> { Items = rows.ToList(), TotalCount = total, PageIndex = page, PageSize = size };
    }

    public Task<PackingListSourceDto?> GetSourceForUpdateAsync(Guid grossDemandId, Guid? warehouseSiteId)
        => context.Session.Connection.QuerySingleOrDefaultAsync<PackingListSourceDto>("""
            SELECT g.Uid AS GrossDemandId,g.Version AS DemandVersion,g.SourceType,g.SourceUid,g.SourceLineId,g.SourceVersion,
                   planHint.NetDemandAllocationId,planHint.NetDemandId,g.ProductId,g.SkuSnapshot AS Sku,g.CustomerSkuSnapshot AS CustomerSku,g.DescriptionSnapshot AS Description,
                   COALESCE(NULLIF(LTRIM(RTRIM(g.UnitSnapshot)),N''),NULLIF(LTRIM(RTRIM(pr.Unit)),N'')) AS Unit,
                   g.CustomerId,c.CustomerCode,c.CustomerName,g.DemandQty AS PlannedQty,COALESCE(sh.ShippedNetQty,0) AS ShippedNetQty,
                   g.DemandQty-COALESCE(sh.ShippedNetQty,0) AS ShippableRemainingQty,COALESCE(rc.SourceInboundNetQty,0) AS SourceInboundNetQty,
                   COALESCE(progress.ReadyQty,0) AS ReadyQty,CASE WHEN g.SourceType='SO' THEN g.SourceUid ELSE NULL END AS SalesOrderId,
                   CASE WHEN g.SourceType='SO' THEN g.SourceLineId ELSE NULL END AS SalesOrderLineId,g.SourceNo AS SalesOrderNo,g.SourceLineNumber AS SalesOrderLineNumber,
                   planHint.PurchaseSupplierId AS SourceSupplierId,planHint.SupplierNameSnapshot AS SourceSupplierName,w.Uid AS WarehouseSiteId,w.Code AS WarehouseCode,w.Name AS WarehouseName,
                   g.RequiredDate,COALESCE(planHint.ExecutionType,0) AS SupplyType,planHint.DemandExecutionLinkId
            FROM dbo.GrossDemand g WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.Customer c ON c.Uid=g.CustomerId LEFT JOIN dbo.Product pr ON pr.Uid=g.ProductId
            OUTER APPLY(
                SELECT candidate.NetDemandId,candidate.NetDemandAllocationId,candidate.PurchaseSupplierId,
                       candidate.SupplierNameSnapshot,candidate.ExecutionType,candidate.DemandExecutionLinkId
                FROM(
                    SELECT n.Uid AS NetDemandId,a.Uid AS NetDemandAllocationId,a.PurchaseSupplierId,a.SupplierNameSnapshot,a.ExecutionType,
                           CASE WHEN COALESCE(dl.LinkCount,0)=1 THEN dl.SingleLinkId ELSE NULL END AS DemandExecutionLinkId,
                           COUNT_BIG(1) OVER() AS CandidateCount
                    FROM dbo.NetDemand n
                    INNER JOIN dbo.NetDemandAllocation a ON a.NetDemandId=n.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
                    OUTER APPLY(SELECT COUNT(*) AS LinkCount,MAX(Uid) AS SingleLinkId FROM dbo.DemandExecutionLink WHERE NetDemandAllocationId=a.Uid AND LinkStatus=0 AND IsDeleted=0) dl
                    WHERE n.GrossDemandId=g.Uid AND n.LifecycleStatus=0 AND n.IsDeleted=0
                ) candidate
                WHERE candidate.CandidateCount=1
            ) planHint
            LEFT JOIN dbo.WarehouseSite w WITH(UPDLOCK,HOLDLOCK) ON w.Uid=COALESCE(@WarehouseSiteId,(SELECT TOP(1) ws.Uid FROM dbo.WarehouseSite ws WHERE ws.SupplierId=planHint.PurchaseSupplierId AND ws.IsDeleted=0 AND ws.IsActive=1 ORDER BY ws.Uid))
            OUTER APPLY(SELECT SUM(l.Quantity) AS ShippedNetQty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.GrossDemandId=g.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND l.IsDeleted=0 AND h.IsDeleted=0) sh
            OUTER APPLY(SELECT SUM(l.Quantity) AS SourceInboundNetQty FROM dbo.InboundDocumentLine l JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId WHERE l.NetDemandAllocationId=planHint.NetDemandAllocationId AND h.PostingStatus=1 AND l.IsDeleted=0 AND h.IsDeleted=0) rc
            OUTER APPLY(SELECT SUM(latest.ReadyQty) AS ReadyQty FROM dbo.DemandExecutionLink link OUTER APPLY(SELECT TOP(1) report.ReadyQty FROM dbo.SupplierProgressReport report WHERE report.PurchaseOrderLineId=link.TargetLineId AND report.IsDeleted=0 ORDER BY report.Sequence DESC,report.Uid DESC) latest WHERE link.NetDemandAllocationId=planHint.NetDemandAllocationId AND link.LinkStatus=0 AND link.IsDeleted=0) progress
            WHERE g.Uid=@GrossDemandId AND g.LifecycleStatus=0 AND g.SourceType IN('SO','CR') AND g.IsDeleted=0
              AND g.CustomerId IS NOT NULL AND g.ProductId IS NOT NULL AND pr.Uid IS NOT NULL AND pr.IsDeleted=0;
            """, new { GrossDemandId = grossDemandId, WarehouseSiteId = warehouseSiteId }, context.Session.Transaction);

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
                   GrossDemandId,NetDemandId,NetDemandAllocationId,SourceDemandVersionSnapshot,SourceVersionSnapshot,DemandExecutionLinkId,SalesOrderId,SalesOrderLineId,SalesOrderNoSnapshot,SalesOrderLineNumberSnapshot,
                   SourceSupplierId,SourceSupplierNameSnapshot,WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,SnapshotRequiredDate,SupplyType,CommandItemId
            FROM dbo.PackingListContributionLine{hint} WHERE PackingListDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY Uid;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();
        var result = new PackingListPlanningSnapshot { Document = document };
        var facts = new List<PackingListPlanningLineSnapshot>();
        foreach (var line in lines.OrderBy(x => x.Uid))
        {
            var row = await context.Session.Connection.QuerySingleOrDefaultAsync<PlanningFactRow>(new CommandDefinition($"""
                SELECT g.CustomerId,g.Version AS DemandVersion,g.SourceVersion,g.DemandQty AS PlannedQty,
                       CAST(CASE WHEN g.LifecycleStatus=0 AND g.SourceType IN('SO','CR') THEN 1 ELSE 0 END AS bit) AS IsSourceActive,
                       CAST(CASE WHEN g.SourceType='SO' THEN 1 ELSE 0 END AS bit) AS HasOrderPoolOwnership,
                       CAST(CASE WHEN p.Uid IS NOT NULL AND ISNULL(p.IsDeleted,0)=0 THEN 1 ELSE 0 END AS bit) AS IsProductActive,
                       CAST(CASE WHEN w.Uid IS NOT NULL AND ISNULL(w.IsActive,0)=1 THEN 1 ELSE 0 END AS bit) AS IsWarehouseActive,
                       COALESCE(sh.Qty,0) AS ShippedNetQty,COALESCE(rc.Qty,0) AS SourceInboundNetQty,COALESCE(progress.ReadyQty,0) AS ReadyQty,
                       COALESCE(i.OnHandQty,0) AS BalanceQty,COALESCE(i.Version,0) AS BalanceVersion
                FROM dbo.GrossDemand g{hint}
                LEFT JOIN dbo.Product p ON p.Uid=@ProductId LEFT JOIN dbo.WarehouseSite w{hint} ON w.Uid=@WarehouseSiteId
                LEFT JOIN dbo.InventoryBalance i{hint} ON i.WarehouseSiteId=@WarehouseSiteId AND i.ProductId=@ProductId AND ISNULL(i.IsDeleted,0)=0
                OUTER APPLY(SELECT SUM(l.Quantity) Qty FROM dbo.OutboundDocumentLine l JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId WHERE l.GrossDemandId=g.Uid AND h.DocumentType=20 AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) sh
                OUTER APPLY(SELECT SUM(l.Quantity) Qty FROM dbo.InboundDocumentLine l JOIN dbo.InboundDocument h ON h.Uid=l.InboundDocumentId WHERE l.NetDemandAllocationId=@AllocationId AND h.PostingStatus=1 AND ISNULL(l.IsDeleted,0)=0 AND ISNULL(h.IsDeleted,0)=0) rc
                OUTER APPLY(SELECT SUM(latest.ReadyQty) ReadyQty FROM dbo.DemandExecutionLink link OUTER APPLY(SELECT TOP(1) report.ReadyQty FROM dbo.SupplierProgressReport report WHERE report.PurchaseOrderLineId=link.TargetLineId AND ISNULL(report.IsDeleted,0)=0 ORDER BY report.Sequence DESC,report.Uid DESC) latest WHERE link.NetDemandAllocationId=@AllocationId AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0) progress
                WHERE g.Uid=@GrossDemandId;
                """, new { GrossDemandId = line.GrossDemandId, AllocationId = line.NetDemandAllocationId, line.WarehouseSiteId, line.ProductId }, context.Session.Transaction, cancellationToken: cancellationToken)) ?? new PlanningFactRow();
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
                Line = line, CustomerId = row.CustomerId, DemandVersion = row.DemandVersion, SourceVersion = row.SourceVersion, PlannedQty = row.PlannedQty,
                HasOrderPoolOwnership = row.HasOrderPoolOwnership,
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
                   l.Marks,l.Note,l.GrossDemandId,l.NetDemandId,l.NetDemandAllocationId,g.Version AS DemandVersion,g.SourceVersion,g.DemandQty-COALESCE(sh.Qty,0) AS ShippableRemainingQty,l.DemandExecutionLinkId,l.SalesOrderId,l.SalesOrderLineId,l.SalesOrderNoSnapshot AS SalesOrderNo,
                   SalesOrderLineNumberSnapshot AS SalesOrderLineNumber,SourceSupplierId,SourceSupplierNameSnapshot AS SourceSupplierName,WarehouseSiteId,WarehouseCodeSnapshot AS WarehouseCode,
                   WarehouseNameSnapshot AS WarehouseName,SnapshotRequiredDate AS RequiredDate,SupplyType,CommandItemId
            FROM dbo.PackingListContributionLine l LEFT JOIN dbo.GrossDemand g ON g.Uid=l.GrossDemandId
            OUTER APPLY(SELECT SUM(ol.Quantity) Qty FROM dbo.OutboundDocumentLine ol JOIN dbo.OutboundDocument oh ON oh.Uid=ol.OutboundDocumentId WHERE ol.GrossDemandId=l.GrossDemandId AND oh.DocumentType=20 AND oh.PostingStatus=1 AND ISNULL(ol.IsDeleted,0)=0 AND ISNULL(oh.IsDeleted,0)=0) sh
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
            if (line.NetDemandAllocationId.HasValue)
                trace.Items.Add(new PackingListTraceItemDto { Stage = "Allocation", Uid = line.NetDemandAllocationId, No = line.SourceSupplierName, Description = line.WarehouseName, Quantity = line.Quantity });
        }
        // 来源需求是交付权威；存在可选 Allocation 时再追加其全部活动执行 Link，避免把单 Link 快照冒充完整追溯。
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
        public int DemandVersion { get; set; }
        public int? SourceVersion { get; set; }
        public bool HasOrderPoolOwnership { get; set; }
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

    /// <summary>仅选择一个稳定的计划执行提示；无结果不影响来源需求进入可交付列表。</summary>
    private const string PlanningExecutionApply = @"
        SELECT candidate.NetDemandId,candidate.NetDemandAllocationId,candidate.PurchaseSupplierId,
               candidate.SupplierNameSnapshot,candidate.SupplierCodeSnapshot,candidate.ExecutionType,
               candidate.WarehouseSiteId,candidate.WarehouseCode,candidate.WarehouseName,candidate.DemandExecutionLinkId
        FROM(
            SELECT n.Uid AS NetDemandId,a.Uid AS NetDemandAllocationId,a.PurchaseSupplierId,
                   a.SupplierNameSnapshot,a.SupplierCodeSnapshot,a.ExecutionType,
                   w.Uid AS WarehouseSiteId,w.Code AS WarehouseCode,w.Name AS WarehouseName,
                   CASE WHEN COALESCE(dl.LinkCount,0)=1 THEN dl.SingleLinkId ELSE NULL END AS DemandExecutionLinkId,
                   COUNT_BIG(1) OVER() AS CandidateCount
            FROM dbo.NetDemand n
            INNER JOIN dbo.NetDemandAllocation a ON a.NetDemandId=n.Uid AND a.LifecycleStatus=0 AND a.IsDeleted=0
            LEFT JOIN dbo.WarehouseSite w ON w.SupplierId=a.PurchaseSupplierId AND w.IsDeleted=0 AND w.IsActive=1
            OUTER APPLY(SELECT COUNT(*) AS LinkCount,MAX(Uid) AS SingleLinkId FROM dbo.DemandExecutionLink WHERE NetDemandAllocationId=a.Uid AND LinkStatus=0 AND IsDeleted=0) dl
            WHERE n.GrossDemandId=g.Uid AND n.LifecycleStatus=0 AND n.IsDeleted=0
        ) candidate
        WHERE candidate.CandidateCount=1";

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
}
