using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Repositories.Inventory;
using Dapper;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>采购管理页新链专用 Dapper 投影；只读取列表与从表需要的当前摘要事实。</summary>
public sealed class PurchaseOrderManagerQueryRepository(RepositoryBaseContext context)
    : IPurchaseOrderManagerQueryRepository
{
    /// <inheritdoc/>
    public async Task<PagedResponseBase<PurchaseOrderManagerListItemDto>> SearchAsync(
        PurchaseOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureFormalReceiptHistoryIsTraceableAsync(cancellationToken);

        // 空集合通过显式开关表示“不限制”；状态值始终走 IN，避免多选筛选退回单值语义。
        var confirmationStatuses = request.ConfirmationStatuses.Count == 0
            ? [-1]
            : request.ConfirmationStatuses.Distinct().Select(static value => (int)value).ToArray();
        var lifecycleStatuses = request.LifecycleStatuses.Count == 0
            ? [-1]
            : request.LifecycleStatuses.Distinct().Select(static value => (int)value).ToArray();
        var readyStatuses = request.ReadyStatuses.Count == 0
            ? [-1]
            : request.ReadyStatuses.Distinct().Select(static value => (int)value).ToArray();
        var receiptStatuses = request.ReceiptStatuses.Count == 0
            ? [-1]
            : request.ReceiptStatuses.Distinct().Select(static value => (int)value).ToArray();
        // 单次查询只取一次北京时间，计数与分页共享同一风险窗口。
        var businessToday = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,
            "China Standard Time").Date;

        var parameters = new
        {
            Keyword = ToLikePattern(request.Keyword),
            CustomerCode = string.IsNullOrWhiteSpace(request.CustomerCode) ? null : request.CustomerCode.Trim(),
            request.SupplierId,
            OrderDateFrom = request.OrderDateFrom?.Date,
            // 用半开区间包含结束日全部时间，保持 OrderDate 列可利用索引。
            OrderDateToExclusive = request.OrderDateTo.HasValue && request.OrderDateTo.Value.Date < DateTime.MaxValue.Date
                ? request.OrderDateTo.Value.Date.AddDays(1) : (DateTime?)null,
            ConfirmationStatuses = confirmationStatuses,
            LimitConfirmationStatuses = request.ConfirmationStatuses.Count > 0,
            LifecycleStatuses = lifecycleStatuses,
            LimitLifecycleStatuses = request.LifecycleStatuses.Count > 0,
            ReadyStatuses = readyStatuses,
            ReceiptStatuses = receiptStatuses,
            LimitReadyStatuses = request.ReadyStatuses.Count > 0,
            LimitReceiptStatuses = request.ReceiptStatuses.Count > 0,
            DueView = (int)request.DueView,
            BusinessToday = businessToday,
            DueSoonExclusive = businessToday.AddDays(8),
            request.PlannedDeliveryDateFrom,
            request.PlannedDeliveryDateTo,
            request.ExpectedCompletionDateFrom,
            request.ExpectedCompletionDateTo,
            PurchaseReceipt = (int)InboundDocumentTypeEnum.PurchaseReceipt,
            FormalPostingStatuses = new[]
            {
                (int)InboundPostingStatusEnum.Posted,
                (int)InboundPostingStatusEnum.Reversed
            },
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
        await EnsureFormalReceiptHistoryIsTraceableAsync(cancellationToken);

        var rows = await context.Session.Connection.QueryAsync<PurchaseOrderManagerLineDto>(new CommandDefinition(
            LinesSql,
            new
            {
                PurchaseOrderId = purchaseOrderId,
                PurchaseReceipt = (int)InboundDocumentTypeEnum.PurchaseReceipt,
                FormalPostingStatuses = new[]
                {
                    (int)InboundPostingStatusEnum.Posted,
                    (int)InboundPostingStatusEnum.Reversed
                },
                CustomerSuppliedMaterialType = MaterialTypesEnum.客供件
            },
            context.Session.Transaction,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    /// <summary>正式采购入库历史若不能定位采购行则拒绝整个管理查询，避免把无法归属的历史静默当成零收货。</summary>
    private async Task EnsureFormalReceiptHistoryIsTraceableAsync(CancellationToken cancellationToken)
    {
        await context.Session.Connection.ExecuteAsync(new CommandDefinition(InboundReceiptHistorySql.Guard, transaction: context.Session.Transaction, cancellationToken: cancellationToken));
        var hasAmbiguousHistory = await context.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.InboundDocumentLine receiptLine
                INNER JOIN dbo.InboundDocument receiptHead ON receiptHead.Uid=receiptLine.InboundDocumentId
                LEFT JOIN dbo.PurchaseOrderLine purchaseLine ON purchaseLine.Uid=receiptLine.SourceLineId
                WHERE receiptHead.DocumentType=@PurchaseReceipt
                  AND receiptHead.PostingStatus IN @FormalPostingStatuses
                  AND ISNULL(receiptHead.IsDeleted,0)=0
                  AND ISNULL(receiptLine.IsDeleted,0)=0
                  AND (receiptLine.SourceLineId IS NULL OR purchaseLine.Uid IS NULL)
            ) THEN 1 ELSE 0 END AS bit);
            """,
            new
            {
                PurchaseReceipt = (int)InboundDocumentTypeEnum.PurchaseReceipt,
                FormalPostingStatuses = new[]
                {
                    (int)InboundPostingStatusEnum.Posted,
                    (int)InboundPostingStatusEnum.Reversed
                }
            },
            context.Session.Transaction,
            cancellationToken: cancellationToken));

        if (hasAmbiguousHistory)
            throw new InvalidOperationException("存在无法定位采购行的正式采购入库历史，请先完成数据核实与迁移。");
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

    // 正式收货先按采购行聚合，再计算行状态和整单状态；不展开需求 Link，避免数量倍增。
    private const string SearchSql = """
        WITH ReceiptFacts AS
        (
            SELECT receiveLine.SourceLineId,
                   SUM(CASE WHEN receiveHead.PostingStatus=1 THEN receiveLine.Quantity ELSE 0 END) AS ReceivedQty,
                   MAX(CASE WHEN receiveHead.PostingStatus=1 AND
                       (NULLIF(LTRIM(RTRIM(receiveLine.UnitSnapshot)),N'') IS NULL OR receiveLine.UnitSnapshot<>purchaseLine.UnitSnapshot)
                       THEN 1 ELSE 0 END) AS HasReceiptUnitMismatch
            FROM dbo.InboundDocumentLine receiveLine
            INNER JOIN dbo.InboundDocument receiveHead ON receiveHead.Uid=receiveLine.InboundDocumentId
            LEFT JOIN dbo.PurchaseOrderLine purchaseLine ON purchaseLine.Uid=receiveLine.SourceLineId
            WHERE receiveHead.DocumentType=@PurchaseReceipt
              AND receiveHead.PostingStatus IN @FormalPostingStatuses
              AND ISNULL(receiveLine.IsDeleted,0)=0
              AND ISNULL(receiveHead.IsDeleted,0)=0
            GROUP BY receiveLine.SourceLineId
        ),
        LineFacts AS
        (
            SELECT l.PurchaseOrderId,l.Uid,l.QTY,l.UnitSnapshot,l.TaxAmount,l.PlannedDeliveryDate,
                   latest.ReadyQty,latest.ExpectedReadyDate,
                   CASE WHEN latest.PurchaseOrderLineId IS NULL THEN 0 ELSE 1 END AS HasProgress,
                   COALESCE(receipt.ReceivedQty,0) AS ReceivedQty,
                   CASE WHEN l.QTY<=0 OR NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NULL
                          THEN 1 ELSE 0 END AS InvalidBaseFact,
                   CASE WHEN l.QTY<=0 OR NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NULL
                          OR latest.ReadyQty<0 OR latest.ReadyQty>l.QTY THEN 1 ELSE 0 END AS InvalidReadyFact,
                   CASE WHEN l.QTY<=0 OR NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NULL
                          OR COALESCE(receipt.HasReceiptUnitMismatch,0)=1
                          OR COALESCE(receipt.ReceivedQty,0)<0 OR COALESCE(receipt.ReceivedQty,0)>l.QTY
                          THEN 1 ELSE 0 END AS InvalidReceiptFact,
                   CASE WHEN l.QTY>0 AND NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),N'') IS NOT NULL
                          AND (latest.ReadyQty IS NULL OR (latest.ReadyQty>=0 AND latest.ReadyQty<l.QTY))
                          AND l.PlannedDeliveryDate IS NOT NULL
                          AND ((@DueView=1 AND l.PlannedDeliveryDate<@BusinessToday)
                            OR (@DueView=2 AND l.PlannedDeliveryDate>=@BusinessToday AND l.PlannedDeliveryDate<@DueSoonExclusive))
                       THEN 1 ELSE 0 END AS DueHit
            FROM dbo.PurchaseOrderLine l
            OUTER APPLY
            (
                SELECT TOP (1) f.PurchaseOrderLineId,f.ReadyQty,f.ExpectedReadyDate
                FROM dbo.SupplierProgressReport f
                WHERE f.PurchaseOrderLineId=l.Uid AND ISNULL(f.IsDeleted,0)=0
                ORDER BY f.Sequence DESC,f.Uid DESC
            ) latest
            LEFT JOIN ReceiptFacts receipt ON receipt.SourceLineId=l.Uid
            WHERE ISNULL(l.IsDeleted,0)=0
        ),
        HeadFacts AS
        (
            SELECT h.Uid AS PurchaseOrderId,h.Version,h.LifecycleStatus,h.OrderNo,h.BoNo,
                   h.CustomerCode AS CustomerCode,h.SupplierName,h.ConfirmationStatus,
                   h.OrderDate,h.ExpectedDate,h.ReplyDate,
                   COUNT(f.Uid) AS LineCount,
                   SUM(f.ReadyQty) AS RawReadyQty,
                   SUM(f.QTY) AS RawTotalQty,
                   SUM(f.ReceivedQty) AS RawReceivedQty,
                   MIN(f.UnitSnapshot) AS MinUnit,MAX(f.UnitSnapshot) AS MaxUnit,
                   COALESCE(SUM(f.InvalidBaseFact),0) AS InvalidBaseCount,
                   COALESCE(SUM(f.InvalidReadyFact),0) AS InvalidReadyCount,
                   COALESCE(SUM(f.InvalidReceiptFact),0) AS InvalidReceiptCount,
                   COALESCE(SUM(CASE WHEN f.Uid IS NOT NULL AND f.ReadyQty IS NULL THEN 1 ELSE 0 END),0) AS UnknownReadyCount,
                   COALESCE(SUM(CASE WHEN f.Uid IS NOT NULL AND f.ReadyQty=0 THEN 1 ELSE 0 END),0) AS ZeroReadyCount,
                   COALESCE(SUM(CASE WHEN f.Uid IS NOT NULL AND f.ReadyQty=f.QTY THEN 1 ELSE 0 END),0) AS FullReadyCount,
                   COALESCE(SUM(CASE WHEN f.Uid IS NOT NULL AND f.ReceivedQty=0 THEN 1 ELSE 0 END),0) AS ZeroReceiptCount,
                   COALESCE(SUM(CASE WHEN f.Uid IS NOT NULL AND f.ReceivedQty=f.QTY THEN 1 ELSE 0 END),0) AS FullReceiptCount,
                   COALESCE(SUM(f.DueHit),0) AS DueHitLineCount,
                   MIN(CASE WHEN f.DueHit=1 THEN f.PlannedDeliveryDate END) AS EarliestDueHitDate,
                   COALESCE(SUM(CASE WHEN f.DueHit=1 AND f.ReadyQty IS NULL THEN 1 ELSE 0 END),0) AS UnconfirmedDueCount,
                   COALESCE(SUM(f.TaxAmount),0) AS TaxTotalAmount,
                   COALESCE(SUM(f.HasProgress),0) AS ReportedLineCount,
                   MAX(f.ExpectedReadyDate) AS ExpectedCompletionDate
            FROM dbo.PurchaseOrderHead h
            LEFT JOIN LineFacts f ON f.PurchaseOrderId=h.Uid
            WHERE ISNULL(h.IsDeleted,0)=0
              AND (@Keyword IS NULL OR h.OrderNo LIKE @Keyword ESCAPE N'~'
                   OR h.BoNo LIKE @Keyword ESCAPE N'~'
                   OR h.DemandSourceNo LIKE @Keyword ESCAPE N'~'
                   OR h.CustomerCode LIKE @Keyword ESCAPE N'~'
                   OR h.SupplierName LIKE @Keyword ESCAPE N'~'
                   OR EXISTS
                   (
                       SELECT 1 FROM dbo.PurchaseOrderLine keywordLine
                       WHERE keywordLine.PurchaseOrderId=h.Uid AND ISNULL(keywordLine.IsDeleted,0)=0
                         AND (keywordLine.Sku LIKE @Keyword ESCAPE N'~'
                              OR keywordLine.SourceNo LIKE @Keyword ESCAPE N'~')
                   ))
              AND (@CustomerCode IS NULL OR h.CustomerCode=@CustomerCode)
              AND (@SupplierId IS NULL OR h.SupplierId=@SupplierId)
              AND (@OrderDateFrom IS NULL OR h.OrderDate>=@OrderDateFrom)
              AND (@OrderDateToExclusive IS NULL OR h.OrderDate<@OrderDateToExclusive)
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
               CASE WHEN facts.InvalidReadyCount>0 OR facts.MinUnit<>facts.MaxUnit OR facts.UnknownReadyCount>0
                         OR facts.LineCount=0 THEN NULL ELSE facts.RawReadyQty END AS ReadyQty,
               CASE WHEN facts.InvalidBaseCount>0 OR facts.MinUnit<>facts.MaxUnit OR facts.LineCount=0
                         THEN NULL ELSE facts.RawTotalQty END AS TotalQty,
               CASE WHEN facts.InvalidReceiptCount>0 OR facts.MinUnit<>facts.MaxUnit OR facts.LineCount=0
                         THEN NULL ELSE CONVERT(decimal(18,6),facts.RawReceivedQty/NULLIF(facts.RawTotalQty,0)) END AS ReceiptProgress,
               CASE WHEN facts.InvalidBaseCount>0 OR facts.MinUnit<>facts.MaxUnit OR facts.LineCount=0
                         THEN NULL ELSE facts.MinUnit END AS UnitSnapshot,
               CASE WHEN facts.LineCount=0 THEN 0 WHEN facts.InvalidReadyCount>0 THEN 9
                    WHEN facts.ReportedLineCount=0 THEN 1 WHEN facts.UnknownReadyCount>0 THEN 2
                    WHEN facts.ZeroReadyCount=facts.LineCount THEN 3
                    WHEN facts.FullReadyCount=facts.LineCount THEN 5 ELSE 4 END AS ReadyStatus,
               CASE WHEN facts.LineCount=0 THEN 0 WHEN facts.InvalidReceiptCount>0 THEN 9
                    WHEN facts.ZeroReceiptCount=facts.LineCount THEN 1
                    WHEN facts.FullReceiptCount=facts.LineCount THEN 3 ELSE 2 END AS ReceiptStatus,
               CONVERT(bit,CASE WHEN facts.UnconfirmedDueCount>0 THEN 1 ELSE 0 END) AS HasUnconfirmedReadyRisk,
               CASE WHEN facts.InvalidBaseCount>0 OR facts.InvalidReadyCount>0 OR facts.InvalidReceiptCount>0
                         THEN N'采购数量、单位、货好或收货事实不一致，需逐行核实。'
                    WHEN facts.MinUnit<>facts.MaxUnit THEN N'采购行单位不同，不提供跨行数量合计。'
                    WHEN facts.UnknownReadyCount>0 THEN N'部分采购行尚未反馈货好数量。'
                    ELSE NULL END AS ReviewReason,
               CONVERT(bit,CASE WHEN facts.LifecycleStatus=@ActiveLifecycle AND facts.ConfirmationStatus=@PendingConfirmation THEN 1 ELSE 0 END) AS CanConfirm,
               CONVERT(bit,CASE WHEN facts.LifecycleStatus=@ActiveLifecycle AND facts.ConfirmationStatus=@Confirmed THEN 1 ELSE 0 END) AS CanCancelConfirmation,
               CASE WHEN facts.LifecycleStatus<>@ActiveLifecycle THEN N'采购单不是活动状态。' ELSE NULL END AS CommandBlockedReason
        INTO #PurchaseOrderManagerHeads
        FROM HeadFacts facts
        WHERE (@ExpectedCompletionDateFrom IS NULL OR facts.ExpectedCompletionDate>=@ExpectedCompletionDateFrom)
          AND (@ExpectedCompletionDateTo IS NULL OR facts.ExpectedCompletionDate<=@ExpectedCompletionDateTo)
          AND (@DueView=0 OR (facts.LifecycleStatus=@ActiveLifecycle AND facts.ConfirmationStatus=@Confirmed AND facts.DueHitLineCount>0));

        DELETE FROM #PurchaseOrderManagerHeads
        WHERE (@LimitReadyStatuses=1 AND ReadyStatus NOT IN @ReadyStatuses)
           OR (@LimitReceiptStatuses=1 AND ReceiptStatus NOT IN @ReceiptStatuses);

        SELECT COUNT(*) FROM #PurchaseOrderManagerHeads;

        SELECT PurchaseOrderId,Version,LifecycleStatus,OrderNo,BoNo,CustomerCode,SupplierName,
               ConfirmationStatus,OrderDate,ExpectedDate,ReplyDate,LineCount,ReadyQty,TotalQty,
               ReceiptProgress,UnitSnapshot,ReadyStatus,ReceiptStatus,ReviewReason,
               DueHitLineCount,EarliestDueHitDate,HasUnconfirmedReadyRisk,
               TaxTotalAmount,ReportedLineCount,ExpectedCompletionDate,
               CanConfirm,CanCancelConfirmation,CommandBlockedReason
        FROM #PurchaseOrderManagerHeads
        ORDER BY OrderDate DESC,OrderNo DESC,PurchaseOrderId DESC
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

        DROP TABLE #PurchaseOrderManagerHeads;
        """;

    // 图片来自当前 Product.MainImage；送至供应商只为客供件投影，其他物料明确返回空值。
    private const string LinesSql = """
        WITH ReceiptFacts AS
        (
            SELECT receiveLine.SourceLineId,SUM(CASE WHEN receiveHead.PostingStatus=1 THEN receiveLine.Quantity ELSE 0 END) AS ReceivedQty
            FROM dbo.InboundDocumentLine receiveLine
            INNER JOIN dbo.InboundDocument receiveHead ON receiveHead.Uid=receiveLine.InboundDocumentId
            WHERE receiveHead.DocumentType=@PurchaseReceipt
              AND receiveHead.PostingStatus IN @FormalPostingStatuses
              AND ISNULL(receiveLine.IsDeleted,0)=0
              AND ISNULL(receiveHead.IsDeleted,0)=0
            GROUP BY receiveLine.SourceLineId
        )
        SELECT l.Uid AS PurchaseOrderLineId,l.Version,COALESCE(l.ProductId,'00000000-0000-0000-0000-000000000000') AS ProductId,
               l.LineNumber,product.MainImage,l.CustomerSku,l.Sku,l.LinkSku,l.SupplierSku,l.DescriptionZh,
               l.FinishZh,l.FeaturesZh,l.MaterialType,l.SupplyType,l.QTY AS Quantity,l.UnitSnapshot,
               COALESCE(receipt.ReceivedQty,0) AS ReceivedQty,
               CASE WHEN l.QTY>COALESCE(receipt.ReceivedQty,0) THEN l.QTY-COALESCE(receipt.ReceivedQty,0) ELSE 0 END AS UnreceivedQty,
               latest.ReadyQty AS ReadyQty,l.PlannedDeliveryDate,
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
        LEFT JOIN ReceiptFacts receipt ON receipt.SourceLineId=l.Uid
        WHERE h.Uid=@PurchaseOrderId
          AND ISNULL(h.IsDeleted,0)=0 AND ISNULL(l.IsDeleted,0)=0
        ORDER BY l.LineNumber,l.Uid;
        """;
}
