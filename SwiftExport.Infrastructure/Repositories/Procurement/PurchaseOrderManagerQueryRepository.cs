using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>采购管理页新链专用 Dapper 投影；只读取列表与从表需要的当前摘要事实。</summary>
public sealed class PurchaseOrderManagerQueryRepository(RepositoryBaseContext context)
    : IPurchaseOrderManagerQueryRepository
{
    /// <inheritdoc/>
    public async Task<PagedResponseBase<PurchaseOrderManagerListItemDto>> SearchAsync(
        PurchaseOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // 空集合通过显式开关表示“不限制”；状态值始终走 IN，避免多选筛选退回单值语义。
        var confirmationStatuses = request.ConfirmationStatuses.Count == 0
            ? [-1]
            : request.ConfirmationStatuses.Distinct().Select(static value => (int)value).ToArray();
        var lifecycleStatuses = request.LifecycleStatuses.Count == 0
            ? [-1]
            : request.LifecycleStatuses.Distinct().Select(static value => (int)value).ToArray();

        var parameters = new
        {
            Keyword = ToLikePattern(request.Keyword),
            OrderNo = ToLikePattern(request.OrderNo),
            SourceNo = ToLikePattern(request.SourceNo),
            SupplierKeyword = ToLikePattern(request.SupplierKeyword),
            Sku = ToLikePattern(request.Sku),
            ConfirmationStatuses = confirmationStatuses,
            LimitConfirmationStatuses = request.ConfirmationStatuses.Count > 0,
            LifecycleStatuses = lifecycleStatuses,
            LimitLifecycleStatuses = request.LifecycleStatuses.Count > 0,
            request.PlannedDeliveryDateFrom,
            request.PlannedDeliveryDateTo,
            request.ExpectedCompletionDateFrom,
            request.ExpectedCompletionDateTo,
            PostedStatus = (int)InboundPostingStatusEnum.Posted,
            PurchaseOrderLineSource = (int)InboundSourceTypeEnum.PurchaseOrderLine,
            ActiveLifecycle = (int)PurchaseOrderLifecycleStatusEnum.Active,
            PendingConfirmation = (int)ConfirmationStatusEnum.待确认,
            Confirmed = (int)ConfirmationStatusEnum.已确认,
            Skip = (request.PageIndex - 1) * request.PageSize,
            Take = request.PageSize
        };

        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            SearchSql,
            parameters,
            context.Session.Transaction,
            cancellationToken: cancellationToken));

        var totalCount = await grid.ReadSingleAsync<int>();
        var items = (await grid.ReadAsync<PurchaseOrderManagerListItemDto>()).ToList();
        return new PagedResponseBase<PurchaseOrderManagerListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PurchaseOrderManagerLineDto>> GetLinesAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Session.Connection.QueryAsync<PurchaseOrderManagerLineDto>(new CommandDefinition(
            LinesSql,
            new
            {
                PurchaseOrderId = purchaseOrderId,
                PostedStatus = (int)InboundPostingStatusEnum.Posted,
                PurchaseOrderLineSource = (int)InboundSourceTypeEnum.PurchaseOrderLine,
                CustomerSuppliedMaterialType = MaterialTypesEnum.客供件
            },
            context.Session.Transaction,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>转义 SQL Server LIKE 元字符；空白输入保持为 null，交由 SQL 跳过该条件。</summary>
    private static string? ToLikePattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return "%" + value.Trim()
            .Replace("~", "~~")
            .Replace("%", "~%")
            .Replace("_", "~_")
            .Replace("[", "~[") + "%";
    }

    // 每行只 OUTER APPLY 最新一条有效跟进和净正式收货，再按采购头聚合；不读取跟进历史、Trace 或库存流水。
    private const string SearchSql = """
        WITH LineFacts AS
        (
            SELECT l.PurchaseOrderId,l.Uid,l.QTY,l.TaxAmount,l.PlannedDeliveryDate,
                   COALESCE(latest.ReadyQty,0) AS ReadyQty,
                   latest.ExpectedReadyDate,
                   CASE WHEN latest.PurchaseOrderLineId IS NULL THEN 0 ELSE 1 END AS HasProgress,
                   COALESCE(receipt.ReceivedQty,0) AS ReceivedQty
            FROM dbo.PurchaseOrderLine l
            OUTER APPLY
            (
                SELECT TOP (1) f.PurchaseOrderLineId,f.ReadyQty,f.ExpectedReadyDate
                FROM dbo.SupplierProgressReport f
                WHERE f.PurchaseOrderLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0
                ORDER BY f.Sequence DESC,f.Uid DESC
            ) latest
            OUTER APPLY
            (
                SELECT SUM(receiveLine.Quantity) AS ReceivedQty
                FROM dbo.InboundDocumentLine receiveLine
                INNER JOIN dbo.InboundDocument receiveHead ON receiveHead.Uid=receiveLine.InboundDocumentId
                WHERE receiveLine.SourceType=@PurchaseOrderLineSource
                  AND receiveLine.SourceUid=l.Uid
                  AND receiveHead.PostingStatus=@PostedStatus
                  AND ISNULL(receiveLine.IsDeleted,0)=0
                  AND ISNULL(receiveHead.IsDeleted,0)=0
            ) receipt
            WHERE ISNULL(l.IsDeleted,0)=0
        ),
        HeadFacts AS
        (
            SELECT h.Uid AS PurchaseOrderId,h.Version,h.LifecycleStatus,h.OrderNo,h.BoNo,
                   h.CustomerCode AS CustomerCode,h.SupplierName,h.ConfirmationStatus,
                   h.OrderDate,h.ExpectedDate,h.ReplyDate,
                   COUNT(f.Uid) AS LineCount,
                   COALESCE(SUM(f.ReadyQty),0) AS ReadyQty,
                   COALESCE(SUM(f.QTY),0) AS TotalQty,
                   CASE WHEN COALESCE(SUM(f.QTY),0)=0 THEN CONVERT(decimal(18,6),0)
                        ELSE CONVERT(decimal(18,6),COALESCE(SUM(f.ReceivedQty),0)/NULLIF(SUM(f.QTY),0)) END AS ReceiptProgress,
                   COALESCE(SUM(f.TaxAmount),0) AS TaxTotalAmount,
                   COALESCE(SUM(f.HasProgress),0) AS ReportedLineCount,
                   MAX(f.ExpectedReadyDate) AS ExpectedCompletionDate
            FROM dbo.PurchaseOrderHead h
            LEFT JOIN LineFacts f ON f.PurchaseOrderId=h.Uid
            WHERE ISNULL(h.IsDeleted,0)=0
              AND (@Keyword IS NULL OR h.OrderNo LIKE @Keyword ESCAPE N'~'
                   OR h.BoNo LIKE @Keyword ESCAPE N'~'
                   OR h.CustomerCode LIKE @Keyword ESCAPE N'~'
                   OR h.SupplierName LIKE @Keyword ESCAPE N'~'
                   OR EXISTS
                   (
                       SELECT 1 FROM dbo.PurchaseOrderLine keywordLine
                       WHERE keywordLine.PurchaseOrderId=h.Uid AND ISNULL(keywordLine.IsDeleted,0)=0
                         AND (keywordLine.Sku LIKE @Keyword ESCAPE N'~'
                              OR keywordLine.SourceNo LIKE @Keyword ESCAPE N'~')
                   ))
              AND (@OrderNo IS NULL OR h.OrderNo LIKE @OrderNo ESCAPE N'~')
              AND (@SourceNo IS NULL OR h.DemandSourceNo LIKE @SourceNo ESCAPE N'~'
                   OR EXISTS
                   (
                       SELECT 1 FROM dbo.PurchaseOrderLine sourceLine
                       WHERE sourceLine.PurchaseOrderId=h.Uid AND ISNULL(sourceLine.IsDeleted,0)=0
                         AND sourceLine.SourceNo LIKE @SourceNo ESCAPE N'~'
                   ))
              AND (@SupplierKeyword IS NULL OR h.SupplierCode LIKE @SupplierKeyword ESCAPE N'~'
                   OR h.SupplierName LIKE @SupplierKeyword ESCAPE N'~')
              AND (@Sku IS NULL OR EXISTS
                   (
                       SELECT 1 FROM dbo.PurchaseOrderLine skuLine
                       WHERE skuLine.PurchaseOrderId=h.Uid AND ISNULL(skuLine.IsDeleted,0)=0
                         AND skuLine.Sku LIKE @Sku ESCAPE N'~'
                   ))
              AND (@LimitConfirmationStatuses=0 OR h.ConfirmationStatus IN @ConfirmationStatuses)
              AND (@LimitLifecycleStatuses=0 OR h.LifecycleStatus IN @LifecycleStatuses)
              AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS
                   (
                       SELECT 1 FROM dbo.PurchaseOrderLine plannedLine
                       WHERE plannedLine.PurchaseOrderId=h.Uid AND ISNULL(plannedLine.IsDeleted,0)=0
                         AND (@PlannedDeliveryDateFrom IS NULL OR plannedLine.PlannedDeliveryDate>=@PlannedDeliveryDateFrom)
                         AND (@PlannedDeliveryDateTo IS NULL OR plannedLine.PlannedDeliveryDate<=@PlannedDeliveryDateTo)
                   ))
            GROUP BY h.Uid,h.Version,h.LifecycleStatus,h.OrderNo,h.BoNo,h.CustomerCode,
                     h.SupplierName,h.ConfirmationStatus,h.OrderDate,h.ExpectedDate,h.ReplyDate
        )
        SELECT facts.*,
               CONVERT(bit,CASE WHEN facts.LifecycleStatus=@ActiveLifecycle AND facts.ConfirmationStatus=@PendingConfirmation THEN 1 ELSE 0 END) AS CanConfirm,
               CONVERT(bit,CASE WHEN facts.LifecycleStatus=@ActiveLifecycle AND facts.ConfirmationStatus=@Confirmed THEN 1 ELSE 0 END) AS CanCancelConfirmation,
               CASE WHEN facts.LifecycleStatus<>@ActiveLifecycle THEN N'采购单不是活动状态。' ELSE NULL END AS CommandBlockedReason
        INTO #PurchaseOrderManagerHeads
        FROM HeadFacts facts
        WHERE (@ExpectedCompletionDateFrom IS NULL OR facts.ExpectedCompletionDate>=@ExpectedCompletionDateFrom)
          AND (@ExpectedCompletionDateTo IS NULL OR facts.ExpectedCompletionDate<=@ExpectedCompletionDateTo);

        SELECT COUNT(*) FROM #PurchaseOrderManagerHeads;

        SELECT PurchaseOrderId,Version,LifecycleStatus,OrderNo,BoNo,CustomerCode,SupplierName,
               ConfirmationStatus,OrderDate,ExpectedDate,ReplyDate,LineCount,ReadyQty,TotalQty,
               ReceiptProgress,TaxTotalAmount,ReportedLineCount,ExpectedCompletionDate,
               CanConfirm,CanCancelConfirmation,CommandBlockedReason
        FROM #PurchaseOrderManagerHeads
        ORDER BY OrderDate DESC,OrderNo DESC,PurchaseOrderId DESC
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

        DROP TABLE #PurchaseOrderManagerHeads;
        """;

    // 图片来自当前 Product.MainImage；送至供应商只为客供件投影，其他物料明确返回空值。
    private const string LinesSql = """
        SELECT l.Uid AS PurchaseOrderLineId,l.Version,COALESCE(l.ProductId,'00000000-0000-0000-0000-000000000000') AS ProductId,
               l.LineNumber,product.MainImage,l.CustomerSku,l.Sku,l.LinkSku,l.SupplierSku,l.DescriptionZh,
               l.FinishZh,l.FeaturesZh,l.MaterialType,l.SupplyType,l.QTY AS Quantity,
               COALESCE(receipt.ReceivedQty,0) AS ReceivedQty,
               CASE WHEN l.QTY>COALESCE(receipt.ReceivedQty,0) THEN l.QTY-COALESCE(receipt.ReceivedQty,0) ELSE 0 END AS UnreceivedQty,
               COALESCE(latest.ReadyQty,0) AS ReadyQty,l.PlannedDeliveryDate,
               latest.ExpectedReadyDate AS ExpectedCompletionDate,
               latest.SupplierReportedDate AS LatestFollowUpDate,latest.CreatedBy AS FollowUpUserName,
               l.UnitPrice,l.TaxPrice,l.UnitAmount,l.TaxAmount,
               CASE WHEN l.MaterialType=@CustomerSuppliedMaterialType THEN l.DeliverySupplierNameSnapshot ELSE NULL END AS DeliverySupplierName,
               l.SourceType,l.SourceNo,l.SourceLineNumber,l.SubNote
        FROM dbo.PurchaseOrderLine l
        INNER JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
        LEFT JOIN dbo.Product product ON product.Uid=l.ProductId AND ISNULL(product.IsDeleted,0)=0
        OUTER APPLY
        (
            SELECT TOP (1) f.ReadyQty,f.ExpectedReadyDate,f.SupplierReportedDate,f.CreatedBy
            FROM dbo.SupplierProgressReport f
            WHERE f.PurchaseOrderLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0
            ORDER BY f.Sequence DESC,f.Uid DESC
        ) latest
        OUTER APPLY
        (
            SELECT SUM(receiveLine.Quantity) AS ReceivedQty
            FROM dbo.InboundDocumentLine receiveLine
            INNER JOIN dbo.InboundDocument receiveHead ON receiveHead.Uid=receiveLine.InboundDocumentId
            WHERE receiveLine.SourceType=@PurchaseOrderLineSource
              AND receiveLine.SourceUid=l.Uid
              AND receiveHead.PostingStatus=@PostedStatus
              AND ISNULL(receiveLine.IsDeleted,0)=0
              AND ISNULL(receiveHead.IsDeleted,0)=0
        ) receipt
        WHERE h.Uid=@PurchaseOrderId
          AND ISNULL(h.IsDeleted,0)=0 AND ISNULL(l.IsDeleted,0)=0
        ORDER BY l.LineNumber,l.Uid;
        """;
}
