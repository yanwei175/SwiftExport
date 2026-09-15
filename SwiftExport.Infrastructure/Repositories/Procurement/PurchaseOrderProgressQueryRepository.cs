using SwiftExport.Infrastructure.Repositories.Inventory;
using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Procurement;

/// <summary>采购跟进页专用 Dapper 投影；按采购行聚合收货并读取不可覆盖的供应商反馈。</summary>
public sealed class PurchaseOrderProgressQueryRepository(RepositoryBaseContext context)
    : IPurchaseOrderProgressQueryRepository
{
    /// <inheritdoc/>
    public async Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(
        PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var confirmationStatuses = request.ConfirmationStatuses.Count == 0
            ? [-1]
            : request.ConfirmationStatuses.Distinct().Select(static value => (int)value).ToArray();
        var lifecycleStatuses = request.LifecycleStatuses.Count == 0
            ? [-1]
            : request.LifecycleStatuses.Distinct().Select(static value => (int)value).ToArray();
        var arrangementStatuses = request.ArrangementStatuses.Count == 0
            ? [-1]
            : request.ArrangementStatuses.Distinct().Select(static value => (int)value).ToArray();

        var parameters = new
        {
            Keyword = ToLikePattern(request.Keyword),
            OrderNo = ToLikePattern(request.OrderNo),
            SupplierKeyword = ToLikePattern(request.SupplierKeyword),
            Sku = ToLikePattern(request.Sku),
            ConfirmationStatuses = confirmationStatuses,
            LifecycleStatuses = lifecycleStatuses,
            ArrangementStatuses = arrangementStatuses,
            LimitConfirmationStatuses = request.ConfirmationStatuses.Count > 0,
            LimitLifecycleStatuses = request.LifecycleStatuses.Count > 0,
            LimitArrangementStatuses = request.ArrangementStatuses.Count > 0,
            request.PlannedDeliveryDateFrom,
            request.PlannedDeliveryDateTo,
            request.ExpectedReadyDateFrom,
            request.ExpectedReadyDateTo,
            Active = (int)PurchaseOrderLifecycleStatusEnum.Active,
            Confirmed = (int)ConfirmationStatusEnum.已确认,
            Skip = (request.PageIndex - 1) * request.PageSize,
            Take = request.PageSize
        };

        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            InboundReceiptHistorySql.Guard + SearchSql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var totalCount = await grid.ReadSingleAsync<int>();
        return new PagedResponseBase<PurchaseOrderProgressListItemDto>
        {
            Items = (await grid.ReadAsync<PurchaseOrderProgressListItemDto>()).ToList(),
            TotalCount = totalCount,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        };
    }

    /// <inheritdoc/>
    public async Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        using var grid = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            InboundReceiptHistorySql.Guard + DetailSql,
            new
            {
                PurchaseOrderId = purchaseOrderId,
                PurchaseReceipt = (int)InboundDocumentTypeEnum.PurchaseReceipt,
                FormalPostingStatuses = new[]
                {
                    (int)InboundPostingStatusEnum.Posted,
                    (int)InboundPostingStatusEnum.Reversed
                }
            },
            context.Session.Transaction,
            cancellationToken: cancellationToken));

        var exists = await grid.ReadSingleAsync<bool>();
        if (!exists) return null;
        var lines = (await grid.ReadAsync<PurchaseOrderProgressLineDto>()).ToList();
        var reports = (await grid.ReadAsync<SupplierProgressReportDto>()).ToList();
        foreach (var line in lines)
        {
            line.ProgressHistory = reports
                .Where(report => report.PurchaseOrderLineId == line.PurchaseOrderLineId)
                .OrderByDescending(report => report.Sequence)
                .ThenByDescending(report => report.Uid)
                .ToList();
            line.LatestProgress = line.ProgressHistory.FirstOrDefault();
        }
        return new PurchaseOrderProgressDetailDto
        {
            PurchaseOrderId = purchaseOrderId,
            Lines = lines
        };
    }

    /// <summary>统一转义 SQL Server LIKE 元字符，避免筛选把用户输入解释为通配表达式。</summary>
    private static string? ToLikePattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return "%" + value.Trim()
            .Replace("~", "~~")
            .Replace("%", "~%")
            .Replace("_", "~_")
            .Replace("[", "~[") + "%";
    }

    private const string SearchSql = """
        WITH LineFacts AS
        (
            SELECT l.PurchaseOrderId,l.Uid,l.QTY,l.PlannedDeliveryDate,
                   latest.ArrangementStatus,latest.ReadyQty,latest.ExpectedReadyDate,
                   CASE WHEN latest.Sequence IS NULL THEN 0 ELSE 1 END AS HasProgress
            FROM dbo.PurchaseOrderLine l
            OUTER APPLY
            (
                SELECT TOP (1) f.Sequence,f.ArrangementStatus,f.ReadyQty,f.ExpectedReadyDate
                FROM dbo.SupplierProgressReport f
                WHERE f.PurchaseOrderLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0
                ORDER BY f.Sequence DESC,f.Uid DESC
            ) latest
            WHERE ISNULL(l.IsDeleted,0)=0
        ),
        HeadFacts AS
        (
            SELECT h.Uid AS PurchaseOrderId,h.Version,h.ConfirmationStatus,h.LifecycleStatus,
                   h.OrderNo,h.OrderDate,h.BoNo,h.SupplierName,h.DemandSourceNo,
                   COUNT(f.Uid) AS LineCount,COALESCE(SUM(f.HasProgress),0) AS ReportedLineCount,
                   SUM(CASE WHEN f.Uid IS NOT NULL AND f.ReadyQty IS NULL THEN 1 ELSE 0 END) AS UnknownReadyQtyLineCount,
                   COALESCE(SUM(f.QTY),0) AS TotalQty,COALESCE(SUM(f.ReadyQty),0) AS LatestReadyQty,
                   MAX(f.ExpectedReadyDate) AS LatestExpectedReadyDate
            FROM dbo.PurchaseOrderHead h
            LEFT JOIN LineFacts f ON f.PurchaseOrderId=h.Uid
            WHERE ISNULL(h.IsDeleted,0)=0
              AND (@Keyword IS NULL OR h.OrderNo LIKE @Keyword ESCAPE N'~'
                   OR h.BoNo LIKE @Keyword ESCAPE N'~'
                   OR h.SupplierName LIKE @Keyword ESCAPE N'~'
                   OR EXISTS (SELECT 1 FROM dbo.PurchaseOrderLine keywordLine
                              WHERE keywordLine.PurchaseOrderId=h.Uid AND ISNULL(keywordLine.IsDeleted,0)=0
                                AND keywordLine.Sku LIKE @Keyword ESCAPE N'~'))
              AND (@OrderNo IS NULL OR h.OrderNo LIKE @OrderNo ESCAPE N'~')
              AND (@SupplierKeyword IS NULL OR h.SupplierCode LIKE @SupplierKeyword ESCAPE N'~'
                   OR h.SupplierName LIKE @SupplierKeyword ESCAPE N'~')
              AND (@Sku IS NULL OR EXISTS (SELECT 1 FROM dbo.PurchaseOrderLine skuLine
                                           WHERE skuLine.PurchaseOrderId=h.Uid AND ISNULL(skuLine.IsDeleted,0)=0
                                             AND skuLine.Sku LIKE @Sku ESCAPE N'~'))
              AND (@LimitConfirmationStatuses=0 OR h.ConfirmationStatus IN @ConfirmationStatuses)
              AND (@LimitLifecycleStatuses=0 OR h.LifecycleStatus IN @LifecycleStatuses)
              AND (@LimitArrangementStatuses=0 OR EXISTS
                   (SELECT 1 FROM LineFacts arrangementLine
                    WHERE arrangementLine.PurchaseOrderId=h.Uid
                      AND COALESCE(arrangementLine.ArrangementStatus,0) IN @ArrangementStatuses))
              AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS
                   (SELECT 1 FROM dbo.PurchaseOrderLine plannedLine
                    WHERE plannedLine.PurchaseOrderId=h.Uid AND ISNULL(plannedLine.IsDeleted,0)=0
                      AND (@PlannedDeliveryDateFrom IS NULL OR plannedLine.PlannedDeliveryDate>=@PlannedDeliveryDateFrom)
                      AND (@PlannedDeliveryDateTo IS NULL OR plannedLine.PlannedDeliveryDate<DATEADD(day,1,@PlannedDeliveryDateTo))))
            GROUP BY h.Uid,h.Version,h.ConfirmationStatus,h.LifecycleStatus,h.OrderNo,h.OrderDate,
                     h.BoNo,h.SupplierName,h.DemandSourceNo
        )
        SELECT facts.PurchaseOrderId,facts.Version,facts.ConfirmationStatus,facts.LifecycleStatus,
               facts.OrderNo,facts.OrderDate,facts.BoNo,facts.SupplierName,facts.DemandSourceNo,
               facts.LineCount,facts.ReportedLineCount,facts.TotalQty,facts.LatestReadyQty,
               facts.UnknownReadyQtyLineCount,facts.LatestExpectedReadyDate,
               CONVERT(bit,CASE WHEN facts.LifecycleStatus=@Active AND facts.ConfirmationStatus=@Confirmed THEN 1 ELSE 0 END) AS CanReportProgress,
               CASE WHEN facts.LifecycleStatus<>@Active THEN N'仅活动采购单允许登记供应商反馈。'
                    WHEN facts.ConfirmationStatus<>@Confirmed THEN N'仅已确认采购单允许登记供应商反馈。'
                    ELSE N'' END AS ReadOnlyReason
        INTO #PurchaseOrderProgressHeads
        FROM HeadFacts facts
        WHERE (@ExpectedReadyDateFrom IS NULL OR facts.LatestExpectedReadyDate>=@ExpectedReadyDateFrom)
          AND (@ExpectedReadyDateTo IS NULL OR facts.LatestExpectedReadyDate<DATEADD(day,1,@ExpectedReadyDateTo));

        SELECT COUNT(*) FROM #PurchaseOrderProgressHeads;
        SELECT PurchaseOrderId,Version,ConfirmationStatus,LifecycleStatus,OrderNo,OrderDate,BoNo,
               SupplierName,DemandSourceNo,LineCount,ReportedLineCount,TotalQty,LatestReadyQty,
               UnknownReadyQtyLineCount,LatestExpectedReadyDate,CanReportProgress,ReadOnlyReason
        FROM #PurchaseOrderProgressHeads
        ORDER BY OrderDate DESC,OrderNo DESC,PurchaseOrderId DESC
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
        DROP TABLE #PurchaseOrderProgressHeads;
        """;

    // 收货事实只按 SourceLineId 聚合，不通过需求 Link 展开；草稿不计入正式数量。
    private const string DetailSql = """
        SELECT CONVERT(bit,CASE WHEN EXISTS
            (SELECT 1 FROM dbo.PurchaseOrderHead h WHERE h.Uid=@PurchaseOrderId AND ISNULL(h.IsDeleted,0)=0)
            THEN 1 ELSE 0 END);

        WITH ReceiptFacts AS
        (
            SELECT receiveLine.SourceLineId,SUM(CASE WHEN receiveHead.PostingStatus=1 THEN receiveLine.Quantity ELSE 0 END) AS ReceivedQty
            FROM dbo.InboundDocumentLine receiveLine
            INNER JOIN dbo.InboundDocument receiveHead ON receiveHead.Uid=receiveLine.InboundDocumentId
            WHERE receiveHead.DocumentType=@PurchaseReceipt
              AND receiveHead.PostingStatus IN @FormalPostingStatuses
              AND ISNULL(receiveLine.IsDeleted,0)=0 AND ISNULL(receiveHead.IsDeleted,0)=0
            GROUP BY receiveLine.SourceLineId
        )
        SELECT l.Uid AS PurchaseOrderLineId,l.Version,l.LineNumber,COALESCE(product.MainImage,N'') AS MainImage,
               l.Sku,l.SupplierSku,l.DescriptionZh,l.QTY AS Quantity,
               COALESCE(receipt.ReceivedQty,0) AS ReceivedQty,l.PlannedDeliveryDate
        FROM dbo.PurchaseOrderLine l
        INNER JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId AND ISNULL(h.IsDeleted,0)=0
        LEFT JOIN dbo.Product product ON product.Uid=l.ProductId AND ISNULL(product.IsDeleted,0)=0
        LEFT JOIN ReceiptFacts receipt ON receipt.SourceLineId=l.Uid
        WHERE l.PurchaseOrderId=@PurchaseOrderId AND ISNULL(l.IsDeleted,0)=0
        ORDER BY l.LineNumber,l.Uid;

        SELECT f.Uid,f.PurchaseOrderLineId,f.Sequence,f.ArrangementStatus,f.ReadyQty,
               f.ExpectedReadyDate,f.SupplierReportedDate,f.Note,f.IsCorrection,
               f.CorrectionReason,f.CommandId,f.CreatedDate,f.CreatedBy
        FROM dbo.SupplierProgressReport f
        INNER JOIN dbo.PurchaseOrderLine l ON l.Uid=f.PurchaseOrderLineId
          AND l.PurchaseOrderId=@PurchaseOrderId AND ISNULL(l.IsDeleted,0)=0
        WHERE ISNULL(f.IsDeleted,0)=0
        ORDER BY l.LineNumber,f.Sequence DESC,f.Uid DESC;
        """;
}
