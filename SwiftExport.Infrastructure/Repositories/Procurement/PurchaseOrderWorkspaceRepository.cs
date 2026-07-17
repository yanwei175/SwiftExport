using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>采购工作台专用仓储；显式映射既有采购物理表，不提供通用 CRUD。</summary>
public sealed class PurchaseOrderWorkspaceRepository(RepositoryBaseContext context) : IPurchaseOrderWorkspaceRepository
{
    private DbSession Session => context.Session;

    public async Task<PagedResponseBase<PurchaseOrderListItemDto>> SearchAsync(PurchaseOrderSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var where = new List<string> { "ISNULL(h.IsDeleted,0)=0" };
        var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
        AddLike(request.Keyword, "(h.OrderNo LIKE @Keyword OR h.BoNo LIKE @Keyword OR COALESCE(h.DemandSourceNo,h.SalesOrderNo) LIKE @Keyword OR h.SupplierCode LIKE @Keyword OR h.SupplierName LIKE @Keyword)", "Keyword", where, parameters);
        AddLike(request.OrderNo, "h.OrderNo LIKE @OrderNo", "OrderNo", where, parameters);
        AddLike(request.SourceNo, "COALESCE(h.DemandSourceNo,h.SalesOrderNo) LIKE @SourceNo", "SourceNo", where, parameters);
        AddLike(request.SupplierKeyword, "(h.SupplierCode LIKE @SupplierKeyword OR h.SupplierName LIKE @SupplierKeyword)", "SupplierKeyword", where, parameters);
        AddLike(request.Sku, "EXISTS(SELECT 1 FROM dbo.PurchaseOrderLine skuLine WHERE skuLine.PurchaseOrderId=h.Uid AND ISNULL(skuLine.IsDeleted,0)=0 AND skuLine.Sku LIKE @Sku)", "Sku", where, parameters);
        if (request.SupplierId.HasValue) { where.Add("h.SupplierId=@SupplierId"); parameters.Add("SupplierId", request.SupplierId.Value); }
        if (request.ConfirmationStatus.HasValue) { where.Add("h.ConfirmationStatus=@ConfirmationStatus"); parameters.Add("ConfirmationStatus", request.ConfirmationStatus.Value); }
        if (request.LifecycleStatus.HasValue) { where.Add("h.LifecycleStatus=@LifecycleStatus"); parameters.Add("LifecycleStatus", request.LifecycleStatus.Value); }
        AddDateRange(request.PlannedDeliveryDateFrom, request.PlannedDeliveryDateTo, "lineFacts.EarliestPlannedDeliveryDate", "Planned", where, parameters);
        AddDateRange(request.ExpectedReadyDateFrom, request.ExpectedReadyDateTo, "lineFacts.LatestExpectedReadyDate", "Ready", where, parameters);
        if (request.ArrangementStatus.HasValue)
        {
            where.Add("EXISTS(SELECT 1 FROM dbo.PurchaseOrderLine arrangementLine OUTER APPLY (SELECT TOP(1) f.ArrangementStatus FROM dbo.FollowUpLog f WHERE f.RefLineType=N'PurchaseOrderLine' AND f.RefLineId=arrangementLine.Uid AND ISNULL(f.IsDeleted,0)=0 ORDER BY f.Sequence DESC,f.Uid DESC) latestArrangement WHERE arrangementLine.PurchaseOrderId=h.Uid AND ISNULL(arrangementLine.IsDeleted,0)=0 AND latestArrangement.ArrangementStatus=@ArrangementStatus)");
            parameters.Add("ArrangementStatus", request.ArrangementStatus.Value);
        }

        var whereSql = string.Join(" AND ", where);
        var sql = $"""
            SELECT COUNT_BIG(1)
            FROM dbo.PurchaseOrderHead h
            OUTER APPLY ({LineFactsSql}) lineFacts
            WHERE {whereSql};

            SELECT h.Uid,h.Version,h.OrderNo,h.BoNo,h.OrderDate,h.ReplyDate,
                   COALESCE(h.ExpectedDate,lineFacts.EarliestPlannedDeliveryDate) AS ExpectedDate,
                   h.SupplierId,h.SupplierCode,h.SupplierName,h.DemandSourceType,h.DemandSourceUid,COALESCE(h.DemandSourceNo,h.SalesOrderNo) AS DemandSourceNo,
                   h.ConfirmationStatus,h.LifecycleStatus,
                   CONVERT(int,COALESCE(lineFacts.LineCount,0)) AS LineCount,
                   COALESCE(lineFacts.TotalQty,0) AS TotalQty,COALESCE(lineFacts.TotalAmount,0) AS TotalAmount,
                   CONVERT(int,COALESCE(lineFacts.ArrangedLineCount,0)) AS ArrangedLineCount,
                   CONVERT(int,COALESCE(lineFacts.ReportedLineCount,0)) AS ReportedLineCount,
                   COALESCE(lineFacts.LatestReadyQty,0) AS LatestReadyQty,lineFacts.LatestExpectedReadyDate,
                   CASE WHEN h.LifecycleStatus=9 THEN N'已作废' WHEN h.LifecycleStatus=8 THEN N'已关闭'
                        WHEN h.ConfirmationStatus=1 THEN N'已确认' ELSE N'待确认' END AS OrderStatusText,
                   N'尚未接入' AS UnshippedQtyText,
                   CAST(CASE WHEN h.SupplierId IS NULL OR h.DemandSourceType IS NULL OR h.DemandSourceUid IS NULL
                                   OR h.DemandSourceNo IS NULL OR h.LifecycleStatus IS NULL OR h.ExecutionRequestId IS NULL
                                   OR NOT EXISTS(SELECT 1 FROM dbo.DemandExecutionLink operableLink WHERE operableLink.TargetHeadId=h.Uid AND operableLink.TargetType=N'PO' AND operableLink.LinkStatus=0 AND ISNULL(operableLink.IsDeleted,0)=0)
                             THEN 1 ELSE 0 END AS bit) AS IsLegacyReadOnly,
                   CASE WHEN h.SupplierId IS NULL THEN N'缺少供应商 Uid'
                        WHEN h.DemandSourceType IS NULL OR h.DemandSourceUid IS NULL OR h.DemandSourceNo IS NULL THEN N'缺少需求来源'
                        WHEN h.LifecycleStatus IS NULL OR h.ExecutionRequestId IS NULL THEN N'缺少新采购生命周期或执行请求'
                        WHEN NOT EXISTS(SELECT 1 FROM dbo.DemandExecutionLink operableLink WHERE operableLink.TargetHeadId=h.Uid AND operableLink.TargetType=N'PO' AND operableLink.LinkStatus=0 AND ISNULL(operableLink.IsDeleted,0)=0) THEN N'缺少有效需求追溯 Link'
                        ELSE NULL END AS ReadOnlyReason,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=0 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanEditDraft,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=0 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanConfirm,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=1 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanCancelConfirmation,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=1 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanReportProgress
            FROM dbo.PurchaseOrderHead h
            OUTER APPLY ({LineFactsSql}) lineFacts
            WHERE {whereSql}
            ORDER BY h.OrderDate DESC,h.Uid DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        using var multi = await Session.Connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, Session.Transaction, cancellationToken: cancellationToken));
        var total = checked((int)await multi.ReadFirstAsync<long>());
        var items = (await multi.ReadAsync<PurchaseOrderListItemDto>()).ToList();
        return new PagedResponseBase<PurchaseOrderListItemDto> { Items = items, TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    public async Task<PurchaseOrderListItemDto?> GetHeadSummaryAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT h.Uid,h.Version,h.OrderNo,h.BoNo,h.OrderDate,h.ReplyDate,COALESCE(h.ExpectedDate,lineFacts.EarliestPlannedDeliveryDate) AS ExpectedDate,
                   h.SupplierId,h.SupplierCode,h.SupplierName,h.DemandSourceType,h.DemandSourceUid,COALESCE(h.DemandSourceNo,h.SalesOrderNo) AS DemandSourceNo,
                   h.ConfirmationStatus,h.LifecycleStatus,CONVERT(int,COALESCE(lineFacts.LineCount,0)) AS LineCount,
                   COALESCE(lineFacts.TotalQty,0) AS TotalQty,COALESCE(lineFacts.TotalAmount,0) AS TotalAmount,
                   CONVERT(int,COALESCE(lineFacts.ArrangedLineCount,0)) AS ArrangedLineCount,
                   CONVERT(int,COALESCE(lineFacts.ReportedLineCount,0)) AS ReportedLineCount,
                   COALESCE(lineFacts.LatestReadyQty,0) AS LatestReadyQty,lineFacts.LatestExpectedReadyDate,
                   CASE WHEN h.LifecycleStatus=9 THEN N'已作废' WHEN h.LifecycleStatus=8 THEN N'已关闭'
                        WHEN h.ConfirmationStatus=1 THEN N'已确认' ELSE N'待确认' END AS OrderStatusText,
                   N'尚未接入' AS UnshippedQtyText,
                   CAST(CASE WHEN h.SupplierId IS NULL OR h.DemandSourceType IS NULL OR h.DemandSourceUid IS NULL OR h.DemandSourceNo IS NULL OR h.LifecycleStatus IS NULL OR h.ExecutionRequestId IS NULL
                                  OR NOT EXISTS(SELECT 1 FROM dbo.DemandExecutionLink l WHERE l.TargetHeadId=h.Uid AND l.TargetType=N'PO' AND l.LinkStatus=0 AND ISNULL(l.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS IsLegacyReadOnly,
                   CASE WHEN h.SupplierId IS NULL THEN N'缺少供应商 Uid' WHEN h.DemandSourceType IS NULL OR h.DemandSourceUid IS NULL OR h.DemandSourceNo IS NULL THEN N'缺少需求来源'
                        WHEN h.LifecycleStatus IS NULL OR h.ExecutionRequestId IS NULL THEN N'缺少新采购生命周期或执行请求'
                        WHEN NOT EXISTS(SELECT 1 FROM dbo.DemandExecutionLink l WHERE l.TargetHeadId=h.Uid AND l.TargetType=N'PO' AND l.LinkStatus=0 AND ISNULL(l.IsDeleted,0)=0) THEN N'缺少有效需求追溯 Link' END AS ReadOnlyReason,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=0 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanEditDraft,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=0 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanConfirm,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=1 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanCancelConfirmation,
                   CAST(CASE WHEN h.LifecycleStatus=0 AND h.ConfirmationStatus=1 AND h.SupplierId IS NOT NULL AND h.ExecutionRequestId IS NOT NULL
                                  AND h.DemandSourceType IS NOT NULL AND h.DemandSourceUid IS NOT NULL AND h.DemandSourceNo IS NOT NULL
                                  AND EXISTS(SELECT 1 FROM dbo.DemandExecutionLink actionLink WHERE actionLink.TargetHeadId=h.Uid AND actionLink.TargetType=N'PO' AND actionLink.LinkStatus=0 AND ISNULL(actionLink.IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit) AS CanReportProgress
            FROM dbo.PurchaseOrderHead h
            OUTER APPLY
            (
                SELECT COUNT_BIG(1) AS LineCount,SUM(l.QTY) AS TotalQty,SUM(COALESCE(l.TaxAmount,l.UnitAmount,0)) AS TotalAmount,
                       MIN(l.PlannedDeliveryDate) AS EarliestPlannedDeliveryDate,
                       SUM(CASE WHEN latest.ArrangementStatus=2 THEN 1 ELSE 0 END) AS ArrangedLineCount,
                       SUM(CASE WHEN latest.Sequence IS NOT NULL THEN 1 ELSE 0 END) AS ReportedLineCount,
                       SUM(COALESCE(latest.ReadyQty,0)) AS LatestReadyQty,MAX(latest.ExpectedReadyDate) AS LatestExpectedReadyDate
                FROM dbo.PurchaseOrderLine l
                OUTER APPLY(SELECT TOP(1) f.Sequence,f.ArrangementStatus,f.ReadyQty,f.ExpectedReadyDate FROM dbo.FollowUpLog f WHERE f.RefLineType=N'PurchaseOrderLine' AND f.RefLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0 ORDER BY f.Sequence DESC,f.Uid DESC) latest
                WHERE l.PurchaseOrderId=h.Uid AND ISNULL(l.IsDeleted,0)=0
            )lineFacts
            WHERE h.Uid=@Uid AND ISNULL(h.IsDeleted,0)=0;
            """;
        return await Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderListItemDto>(new CommandDefinition(sql, new { Uid = uid }, Session.Transaction, cancellationToken: cancellationToken));
    }

    public Task<NetDemandPurchaseDraftHead?> GetHeadAsync(Guid uid, CancellationToken cancellationToken = default)
        => Session.Connection.QuerySingleOrDefaultAsync<NetDemandPurchaseDraftHead>(new CommandDefinition($"SELECT {HeadColumns} FROM dbo.PurchaseOrderHead h WHERE h.Uid=@Uid AND ISNULL(h.IsDeleted,0)=0;", new { Uid = uid }, Session.Transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<PurchaseOrderLineDetailDto>> GetLineDetailsAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT l.Uid,l.Version,l.LineNumber,l.ProductId,l.Sku,l.CustomerSku,l.LinkSku,l.SupplierSku,l.DescriptionZh,
                   l.FinishZh,l.FeaturesZh,l.MaterialType,l.SupplyType,COALESCE(p.MainImage,N'') AS MainImage,l.QTY AS Quantity,
                   l.UnitPrice,l.TaxPrice,l.UnitAmount,l.TaxAmount,l.PlannedDeliveryDate,l.DeliveryTargetAllocationId,
                   l.DeliveryTargetSupplierId,l.DeliverySupplierNameSnapshot AS DeliverySupplierName,l.SubNote,
                   COALESCE(linkFacts.LinkedQty,0) AS LinkedQty,COALESCE(l.RecQty,0) AS RecQty,N'尚未接入' AS UnreceivedQtyText
            FROM dbo.PurchaseOrderLine l
            LEFT JOIN dbo.Product p ON p.Uid=l.ProductId AND ISNULL(p.IsDeleted,0)=0
            OUTER APPLY(SELECT SUM(link.AllocatedQty-link.ReleasedQty) AS LinkedQty FROM dbo.DemandExecutionLink link WHERE link.TargetLineId=l.Uid AND link.TargetHeadId=@Uid AND link.TargetType=N'PO' AND link.LinkStatus=0 AND ISNULL(link.IsDeleted,0)=0)linkFacts
            WHERE l.PurchaseOrderId=@Uid AND ISNULL(l.IsDeleted,0)=0
            ORDER BY l.LineNumber,l.Uid;
            """;
        var rows = await Session.Connection.QueryAsync<PurchaseOrderLineDetailDto>(new CommandDefinition(sql, new { Uid = uid }, Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public Task<IReadOnlyList<PurchaseOrderSourceContributionDto>> GetContributionsAsync(Guid uid, CancellationToken cancellationToken = default)
        => GetContributionsCoreAsync(uid, false, cancellationToken);

    public Task<IReadOnlyList<PurchaseOrderSourceContributionDto>> GetContributionsForUpdateAsync(Guid uid, CancellationToken cancellationToken = default)
        => GetContributionsCoreAsync(uid, true, cancellationToken);

    private async Task<IReadOnlyList<PurchaseOrderSourceContributionDto>> GetContributionsCoreAsync(Guid uid, bool forUpdate, CancellationToken cancellationToken)
    {
        var lockHint = forUpdate ? "WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
        var activeClause = forUpdate ? "AND link.LinkStatus=0" : string.Empty;
        var sql = $"""
            SELECT link.Uid AS LinkId,line.Uid AS PurchaseOrderLineId,link.NetDemandAllocationId AS AllocationId,nd.Uid AS NetDemandId,gd.Uid AS GrossDemandId,
                   gd.SourceType,gd.SourceUid,gd.SourceNo,gd.SourceLineNumber,nd.Sku,link.AllocatedQty,link.ReleasedQty,
                   link.AllocatedQty-link.ReleasedQty AS EffectiveQty,link.DemandRequiredDate,link.PlannedDeliveryDate
            FROM dbo.DemandExecutionLink link {lockHint}
            INNER JOIN dbo.NetDemandAllocation allocation ON allocation.Uid=link.NetDemandAllocationId AND ISNULL(allocation.IsDeleted,0)=0
            INNER JOIN dbo.DependentDemand nd ON nd.Uid=allocation.NetDemandId AND nd.Uid=link.DependentDemandId AND ISNULL(nd.IsDeleted,0)=0
            INNER JOIN dbo.IndependentDemand gd ON gd.Uid=nd.IndependentDemandId AND ISNULL(gd.IsDeleted,0)=0
            INNER JOIN dbo.PurchaseOrderLine line ON line.Uid=link.TargetLineId AND line.PurchaseOrderId=@Uid AND ISNULL(line.IsDeleted,0)=0
            WHERE link.TargetHeadId=@Uid AND link.TargetType=N'PO' {activeClause} AND ISNULL(link.IsDeleted,0)=0
            ORDER BY line.LineNumber,gd.SourceLineNumber,allocation.Uid,link.Uid;
            """;
        var rows = await Session.Connection.QueryAsync<PurchaseOrderSourceContributionDto>(new CommandDefinition(sql, new { Uid = uid }, Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public Task<NetDemandPurchaseDraftHead?> GetHeadForUpdateAsync(Guid uid)
        => Session.Connection.QuerySingleOrDefaultAsync<NetDemandPurchaseDraftHead>($"SELECT {HeadColumns} FROM dbo.PurchaseOrderHead h WITH(UPDLOCK,HOLDLOCK) WHERE h.Uid=@Uid AND ISNULL(h.IsDeleted,0)=0;", new { Uid = uid }, Session.Transaction);

    public async Task<IReadOnlyList<NetDemandPurchaseDraftLine>> GetLinesForUpdateAsync(Guid uid)
    {
        var rows = await Session.Connection.QueryAsync<NetDemandPurchaseDraftLine>($"SELECT {LineColumns} FROM dbo.PurchaseOrderLine l WITH(UPDLOCK,HOLDLOCK) WHERE l.PurchaseOrderId=@Uid AND ISNULL(l.IsDeleted,0)=0 ORDER BY l.LineNumber,l.Uid;", new { Uid = uid }, Session.Transaction);
        return [.. rows];
    }

    public Task<bool> IsActiveSupplierAsync(Guid supplierId)
        => Session.Connection.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Supplier WHERE Uid=@SupplierId AND ISNULL(IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit);", new { SupplierId = supplierId }, Session.Transaction);

    public Task<bool> HasDownstreamFactsAsync(Guid uid)
        => Session.Connection.ExecuteScalarAsync<bool>("""
            SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.PurchaseOrderLine l WITH(UPDLOCK,HOLDLOCK) WHERE l.PurchaseOrderId=@Uid AND ISNULL(l.IsDeleted,0)=0 AND (ISNULL(l.RecQty,0)>0 OR l.LastRecDate IS NOT NULL))
             OR EXISTS(SELECT 1 FROM dbo.PurchaseReceiveLine r WITH(UPDLOCK,HOLDLOCK) WHERE ISNULL(r.IsDeleted,0)=0 AND (r.PoLineId IN(SELECT l.Uid FROM dbo.PurchaseOrderLine l WHERE l.PurchaseOrderId=@Uid AND ISNULL(l.IsDeleted,0)=0) OR (r.PoLineId IS NULL AND r.PoNo=(SELECT OrderNo FROM dbo.PurchaseOrderHead WHERE Uid=@Uid))))
             OR EXISTS(SELECT 1 FROM dbo.InventoryTransaction i WITH(UPDLOCK,HOLDLOCK) WHERE ISNULL(i.IsDeleted,0)=0 AND (i.BizLineId IN(SELECT l.Uid FROM dbo.PurchaseOrderLine l WHERE l.PurchaseOrderId=@Uid AND ISNULL(l.IsDeleted,0)=0) OR i.RefNo=(SELECT OrderNo FROM dbo.PurchaseOrderHead WHERE Uid=@Uid))) THEN 1 ELSE 0 END AS bit);
            """, new { Uid = uid }, Session.Transaction);

    public async Task<int> UpdateDraftAsync(NetDemandPurchaseDraftHead head, IReadOnlyList<NetDemandPurchaseDraftLine> lines, int expectedVersion)
    {
        var affected = 0;
        const string lineSql = """
            UPDATE dbo.PurchaseOrderLine SET PlannedDeliveryDate=@PlannedDeliveryDate,SupplierSku=@SupplierSku,
              DescriptionZh=@DescriptionZh,UnitPrice=@UnitPrice,TaxPrice=@TaxPrice,
              UnitAmount=CASE WHEN @UnitPrice IS NULL THEN NULL ELSE @UnitPrice*QTY END,
              TaxAmount=CASE WHEN @TaxPrice IS NULL THEN NULL ELSE @TaxPrice*QTY END,
              SubNote=@SubNote,UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
            WHERE Uid=@Uid AND PurchaseOrderId=@PurchaseOrderId AND Version=@Version AND ISNULL(IsDeleted,0)=0;
            """;
        foreach (var line in lines)
        {
            var count = await Session.Connection.ExecuteAsync(lineSql, new { line.Uid, line.PurchaseOrderId, line.Version, line.PlannedDeliveryDate, line.SupplierSku, line.DescriptionZh, line.UnitPrice, line.TaxPrice, line.SubNote, Now = DateTime.Now, UserName = context.CurrentUser.UserName }, Session.Transaction);
            if (count != 1) return 0;
            affected += count;
        }
        var headCount = await Session.Connection.ExecuteAsync("""
            UPDATE h SET BoNo=@BoNo,CustomerPoNo=@CustomerPoNo,SoPerson=@SoPerson,PoPerson=@PoPerson,ReplyDate=@ReplyDate,
              InletrHoseType=@InletrHoseType,AeratorType=@AeratorType,PalletType=@PalletType,BoxType=@BoxType,
              LabelType=@LabelType,IMType=@IMType,CartonType=@CartonType,LogoType=@LogoType,ZheKouBeiZhu=@ZheKouBeiZhu,
              Remark=@Remark,LineCount=f.LineCount,TotalQty=f.TotalQty,TotalAmountLocalCurrency=f.TotalAmount,
              ExpectedDate=f.ExpectedDate,UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
            FROM dbo.PurchaseOrderHead h
            CROSS APPLY(SELECT COUNT_BIG(1) LineCount,SUM(l.QTY) TotalQty,SUM(COALESCE(l.TaxPrice,l.UnitPrice,0)*l.QTY) TotalAmount,MIN(l.PlannedDeliveryDate) ExpectedDate FROM dbo.PurchaseOrderLine l WHERE l.PurchaseOrderId=h.Uid AND ISNULL(l.IsDeleted,0)=0)f
            WHERE h.Uid=@Uid AND h.Version=@ExpectedVersion AND h.LifecycleStatus=0 AND h.ConfirmationStatus=0 AND ISNULL(h.IsDeleted,0)=0;
            """, new { head.Uid, ExpectedVersion = expectedVersion, head.BoNo, head.CustomerPoNo, head.SoPerson, head.PoPerson,
                head.ReplyDate, head.InletrHoseType, head.AeratorType, head.PalletType, head.BoxType, head.LabelType, head.IMType,
                head.CartonType, head.LogoType, head.ZheKouBeiZhu, head.Remark, Now = DateTime.Now, UserName = context.CurrentUser.UserName }, Session.Transaction);
        return headCount == 1 ? affected + 1 : 0;
    }

    public Task<int> SetConfirmationAsync(Guid uid, int expectedVersion, bool confirmed, DateTime? confirmedDate, string? confirmedBy, string updatedBy)
        => Session.Connection.ExecuteAsync("""
            UPDATE dbo.PurchaseOrderHead SET ConfirmationStatus=@Status,ConfirmedDate=@ConfirmedDate,ConfirmedBy=@ConfirmedBy,
              UpdatedDate=@Now,UpdatedBy=@UpdatedBy,Version=Version+1
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=0 AND ConfirmationStatus=@ExpectedStatus
              AND SupplierId IS NOT NULL AND ExecutionRequestId IS NOT NULL AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid, ExpectedVersion = expectedVersion, Status = confirmed ? ConfirmationStatusEnum.已确认 : ConfirmationStatusEnum.待确认, ExpectedStatus = confirmed ? ConfirmationStatusEnum.待确认 : ConfirmationStatusEnum.已确认, ConfirmedDate = confirmedDate, ConfirmedBy = confirmedBy, Now = DateTime.Now, UpdatedBy = updatedBy }, Session.Transaction);

    private const string LineFactsSql = """
        SELECT COUNT_BIG(1) AS LineCount,SUM(l.QTY) AS TotalQty,SUM(COALESCE(l.TaxAmount,l.UnitAmount,0)) AS TotalAmount,
               MIN(l.PlannedDeliveryDate) AS EarliestPlannedDeliveryDate,
               SUM(CASE WHEN latest.ArrangementStatus=2 THEN 1 ELSE 0 END) AS ArrangedLineCount,
               SUM(CASE WHEN latest.Sequence IS NOT NULL THEN 1 ELSE 0 END) AS ReportedLineCount,
               SUM(COALESCE(latest.ReadyQty,0)) AS LatestReadyQty,MAX(latest.ExpectedReadyDate) AS LatestExpectedReadyDate
        FROM dbo.PurchaseOrderLine l
        OUTER APPLY(SELECT TOP(1) f.Sequence,f.ArrangementStatus,f.ReadyQty,f.ExpectedReadyDate FROM dbo.FollowUpLog f WHERE f.RefLineType=N'PurchaseOrderLine' AND f.RefLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0 ORDER BY f.Sequence DESC,f.Uid DESC) latest
        WHERE l.PurchaseOrderId=h.Uid AND ISNULL(l.IsDeleted,0)=0
        """;
    private const string HeadColumns = "h.Uid,h.Version,h.IsDeleted,h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy,h.OrderStatus,h.ConfirmationStatus,h.LifecycleStatus,h.OrderNo,h.BoNo,h.SalesOrderNo,h.CustomerPoNo,h.CustomerCode,h.Currency,h.SupplierCode,h.SupplierName,h.SoPerson,h.PoPerson,h.InletrHoseType,h.AeratorType,h.PalletType,h.BoxType,h.LabelType,h.IMType,h.CartonType,h.LogoType,h.ReviewedBy,h.PoFolder,h.ZheKouBeiZhu,h.SupplierId,h.DemandSourceType,h.DemandSourceUid,h.DemandSourceNo,h.ExecutionRequestId,h.Remark,h.IsLocked,h.LineCount,h.TotalAmountLocalCurrency,h.TotalQty,h.TotalShippedQty,h.TotalUnshippedQty,h.TotalReadyQty,h.OrderDate,h.ExpectedDate,h.ReplyDate,h.ConfirmedDate,h.ConfirmedBy";
    private const string LineColumns = "l.Uid,l.Version,l.IsDeleted,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy,l.ReadyStatus,l.LineNumber,l.PurchaseOrderId,l.OrderNo,l.ProductId,l.Sku,l.CustomerSku,l.LinkSku,l.SupplierSku,l.DescriptionZh,l.FinishZh,l.FeaturesZh,l.UnitPrice,l.TaxPrice,l.QTY,l.UnitAmount,l.TaxAmount,l.PlannedDeliveryDate,l.DeliveryTargetAllocationId,l.DeliveryTargetSupplierId,l.DeliverySupplierCodeSnapshot,l.DeliverySupplierNameSnapshot,l.SubNote,l.SupplyType,l.MaterialType,l.DeliveryTo,l.SourceType,l.SourceNo,l.SourceUid,l.SourceLineId,l.SourceLineNumber,l.ReadyQty,l.RecQty,l.LastRecDate";

    private static void AddLike(string? value, string clause, string parameterName, ICollection<string> where, DynamicParameters parameters)
    { if (!string.IsNullOrWhiteSpace(value)) { where.Add(clause); parameters.Add(parameterName, "%" + value.Trim() + "%"); } }
    private static void AddDateRange(DateTime? from, DateTime? to, string column, string prefix, ICollection<string> where, DynamicParameters parameters)
    { if (from.HasValue) { where.Add(column + ">=@" + prefix + "From"); parameters.Add(prefix + "From", from.Value); } if (to.HasValue) { where.Add(column + "<@" + prefix + "To"); parameters.Add(prefix + "To", to.Value.Date.AddDays(1)); } }
}
