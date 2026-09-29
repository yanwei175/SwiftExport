using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

using SwiftExport.Core.Interfaces.SalesOrders.Query;
namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>使用请求级连接查询销售订单列表和详情，保持原筛选、排序和投影 SQL。</summary>
public sealed partial class SalesOrderQueryRepository(RepositoryBaseContext context) : DapperRepository<SalesOrderHeader>(context), ISalesOrderQueryRepository
{
    /// <summary>按业务日期计算销售行交付事实，在同一候选集合中完成筛选、总数和稳定分页。</summary>
    public async Task<PagedResponseBase<SalesOrderManagerListItemDto>> SearchManagerAsync(
        SalesOrderManagerSearchRequest request, DateTime businessToday,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var today = businessToday.Date;
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize, PageSize = pageSize,
            Today = today, SoonEnd = today.AddDays(7),
            QuickView = (int)request.QuickView,
            SalesSourceType = DocumentTypeStaticsEnum.SalesOrder,
            OrderOpen = SalesOrderDocumentStatusEnum.Open,
            OrderClosed = SalesOrderDocumentStatusEnum.Closed,
            Pending = ConfirmationStatusEnum.待确认,
            Confirmed = ConfirmationStatusEnum.已确认,
            DemandActive = GrossDemandLifecycleStatusEnum.Active,
            DemandVoided = GrossDemandLifecycleStatusEnum.Voided,
            Unposted = PackingListPostingStatusEnum.Unposted,
            Posted = PackingListPostingStatusEnum.Posted,
            Reversed = PackingListPostingStatusEnum.Reversed
        });
        var where = new StringBuilder("WHERE h.IsDeleted=0");
        var customerCodes = request.CustomerCodes?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (customerCodes.Length > 0)
        {
            where.Append(" AND h.CustomerCode IN @CustomerCodes");
            parameters.Add("CustomerCodes", customerCodes);
        }
        AddLikeFilter(where, parameters, "h.OrderNo", "OrderNo", request.OrderNo);
        AddLikeFilter(where, parameters, "h.CustomerPoNo", "CustomerPoNo", request.CustomerPoNo);
        AddLikeFilter(where, parameters, "h.InvoiceNo", "InvoiceNo", request.InvoiceNo);
        if (request.CustomerId is { } customerId && customerId != Guid.Empty)
        {
            where.Append(" AND h.CustomerId=@CustomerId");
            parameters.Add("CustomerId", customerId);
        }
        AddDateRange(where, parameters, "h.OrderDate", "OrderDate", request.OrderDateFrom, request.OrderDateTo);
        if (request.DocumentStatuses?.Count > 0)
        {
            where.Append(" AND h.DocumentStatus IN @DocumentStatuses");
            parameters.Add("DocumentStatuses", request.DocumentStatuses.Distinct().ToArray());
        }
        if (request.ConfirmationStatuses?.Count > 0)
        {
            where.Append(" AND h.ConfirmationStatus IN @ConfirmationStatuses");
            parameters.Add("ConfirmationStatuses", request.ConfirmationStatuses.Distinct().ToArray());
        }
        var customerSkus = request.CustomerSkus?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (customerSkus.Length > 0)
        {
            where.Append(@" AND EXISTS (SELECT 1 FROM dbo.SalesOrderLine skuLine
                WHERE skuLine.SalesOrderId=h.Uid AND skuLine.IsDeleted=0
                  AND skuLine.CustomerSku IN @CustomerSkus)");
            parameters.Add("CustomerSkus", customerSkus);
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.Append(@" AND (h.OrderNo LIKE @Keyword ESCAPE '~'
                OR h.CustomerCode LIKE @Keyword ESCAPE '~'
                OR h.CustomerPoNo LIKE @Keyword ESCAPE '~'
                OR h.InvoiceNo LIKE @Keyword ESCAPE '~'
                OR c.CustomerName LIKE @Keyword ESCAPE '~'
                OR EXISTS (SELECT 1 FROM dbo.SalesOrderLine keywordLine
                    WHERE keywordLine.SalesOrderId=h.Uid AND keywordLine.IsDeleted=0
                      AND (keywordLine.Sku LIKE @Keyword ESCAPE '~'
                        OR keywordLine.CustomerSku LIKE @Keyword ESCAPE '~'
                        OR keywordLine.LinkSku LIKE @Keyword ESCAPE '~'
                        OR keywordLine.DescriptionEn LIKE @Keyword ESCAPE '~')))");
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        if (request.EffectiveDeliveryDateFrom.HasValue || request.EffectiveDeliveryDateTo.HasValue)
        {
            where.Append(@" AND EXISTS (SELECT 1 FROM dbo.SalesOrderLine dueLine
                WHERE dueLine.SalesOrderId=h.Uid AND dueLine.IsDeleted=0
                  AND (@EffectiveDueFrom IS NULL OR COALESCE(dueLine.ExpectedDeliveryDate,h.DeliveryDate)>=@EffectiveDueFrom)
                  AND (@EffectiveDueTo IS NULL OR COALESCE(dueLine.ExpectedDeliveryDate,h.DeliveryDate)<@EffectiveDueTo))");
        }
        parameters.Add("EffectiveDueFrom", request.EffectiveDeliveryDateFrom?.Date);
        parameters.Add("EffectiveDueTo", request.EffectiveDeliveryDateTo?.Date.AddDays(1));

        const string quickFilter = @"(@QuickView=0
            OR (@QuickView=1 AND h.ConfirmationStatus=@Pending)
            OR (@QuickView=2 AND f.LineCount>0 AND f.ReviewCount=0 AND f.ShippedQty=0 AND f.OutstandingLineCount>0)
            OR (@QuickView=3 AND f.LineCount>0 AND f.ReviewCount=0 AND f.ShippedQty>0 AND f.OutstandingLineCount>0)
            OR (@QuickView=4 AND f.LineCount>0 AND f.ReviewCount=0 AND f.OutstandingLineCount=0)
            OR (@QuickView=5 AND f.DueSoonLineCount>0)
            OR (@QuickView=6 AND f.OverdueLineCount>0)
            OR (@QuickView=7 AND h.DocumentStatus=@OrderClosed AND f.OutstandingLineCount>0)
            OR (@QuickView=8 AND (f.ReviewCount>0 OR f.LineCount=0)))";
        var sql = $@"
            SELECT h.Uid,h.Version,h.DocumentStatus,h.ConfirmationStatus,h.CustomerId,h.OrderNo,h.DeliveryDate
            INTO #CandidateOrders
            FROM dbo.SalesOrderHeader h
            LEFT JOIN dbo.Customer c ON c.Uid=h.CustomerId AND c.IsDeleted=0
            {where};
            {SalesOrderFulfillmentQuerySql.CreateLineFacts}
            {SalesOrderFulfillmentQuerySql.CreateOrderFacts}
            SELECT f.Uid INTO #FilteredOrders
            FROM #SalesOrderFacts f
            INNER JOIN #CandidateOrders h ON h.Uid=f.Uid
            WHERE {quickFilter};
            SELECT COUNT(1) FROM #FilteredOrders;
            SELECT h.Uid,h.Version,h.DocumentStatus,h.ConfirmationStatus,
                   CASE WHEN f.LineCount=0 OR f.ReviewCount>0 THEN NULL
                        WHEN f.OutstandingLineCount=0 THEN 2
                        WHEN f.ShippedQty=0 THEN 0 ELSE 1 END AS FulfillmentStatus,
                   h.OrderNo,ISNULL(h.CustomerId,'00000000-0000-0000-0000-000000000000') AS CustomerId,
                   ISNULL(h.CustomerCode,N'') AS CustomerCode,ISNULL(c.CustomerName,N'') AS CustomerName,
                   ISNULL(h.CustomerPoNo,N'') AS CustomerPoNo,ISNULL(h.InvoiceNo,N'') AS InvoiceNo,
                   ISNULL(h.Currency,N'') AS Currency,ISNULL(h.ExchangeRate,0) AS ExchangeRate,
                   CASE WHEN f.LineCount>0 AND f.UnknownCount=0 AND f.UnitCount=1 AND f.MissingUnitCount=0
                        THEN f.OrderedQty END AS TotalQty,
                   CASE WHEN f.LineCount>0 AND f.UnknownCount=0 AND f.UnitCount=1 AND f.MissingUnitCount=0
                        THEN f.ShippedQty END AS TotalShippedQty,
                   CASE WHEN f.LineCount>0 AND f.UnknownCount=0 AND f.UnitCount=1 AND f.MissingUnitCount=0
                        THEN f.UnshippedQty END AS TotalUnshippedQty,
                   CASE WHEN f.LineCount>0 AND f.UnknownCount=0 AND f.UnitCount=1 AND f.MissingUnitCount=0
                        THEN f.UnitSnapshot END AS UnitSnapshot,
                   f.EarliestOpenDueDate,f.HasUndatedOpenLine,f.OverdueLineCount,f.DueSoonLineCount,
                   f.EarliestOverdueDate,f.EarliestDueSoonDate,f.PendingPastDueLineCount,
                   CONVERT(bit,CASE WHEN f.LineCount=0 OR f.ReviewCount>0 THEN 1 ELSE 0 END) AS NeedsReview,
                   CASE WHEN f.LineCount=0 THEN N'无有效销售行'
                        ELSE ISNULL(reasons.ReviewReason,N'') END AS ReviewReason,
                   ISNULL(h.TotalAmountOriginalCurrency,0) AS TotalAmountOriginalCurrency,
                   ISNULL(h.TotalAmountLocalCurrency,0) AS TotalAmountLocalCurrency,
                   h.OrderDate,h.DeliveryDate,h.ReplyDate,h.CreatedDate,
                   ISNULL(h.CreatedBy,N'') AS CreatedBy,h.UpdatedDate,ISNULL(h.UpdatedBy,N'') AS UpdatedBy
            FROM #FilteredOrders filtered
            INNER JOIN #SalesOrderFacts f ON f.Uid=filtered.Uid
            INNER JOIN dbo.SalesOrderHeader h ON h.Uid=f.Uid
            LEFT JOIN dbo.Customer c ON c.Uid=h.CustomerId AND c.IsDeleted=0
            OUTER APPLY (
                SELECT STUFF((SELECT N'、'+distinctReasons.ReviewReason
                    FROM (SELECT DISTINCT lf.ReviewReason FROM #SalesLineFacts lf
                          WHERE lf.SalesOrderId=f.Uid AND lf.ReviewReason IS NOT NULL) distinctReasons
                    ORDER BY distinctReasons.ReviewReason
                    FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,1,N'') AS ReviewReason
            ) reasons
            ORDER BY CASE WHEN @QuickView=6 THEN f.EarliestOverdueDate
                          WHEN @QuickView=5 THEN f.EarliestDueSoonDate END,
                     h.CreatedDate DESC,h.Uid DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        using var multi = await Connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, transaction: Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<SalesOrderManagerListItemDto>
        {
            TotalCount = await multi.ReadSingleAsync<int>(),
            Items = (await multi.ReadAsync<SalesOrderManagerListItemDto>()).ToList(),
            PageIndex = pageIndex, PageSize = pageSize
        };
    }
    /// <summary>
    /// 读取销售订单头与有效行组成的 Detail，并复用管理查询的正式交付口径。
    /// </summary>
    public async Task<SalesOrderDetailDto?> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT h.Uid,h.Version,h.DocumentStatus,h.ConfirmationStatus,h.CustomerId,h.OrderNo,h.DeliveryDate
            INTO #CandidateOrders FROM dbo.SalesOrderHeader h
            WHERE h.Uid=@Uid AND h.IsDeleted=0;
            {SalesOrderFulfillmentQuerySql.CreateLineFacts}
            SELECT * FROM dbo.SalesOrderHeader WHERE Uid=@Uid AND IsDeleted=0;
            SELECT
                l.Uid, l.Version, l.CreatedDate, l.CreatedBy, l.UpdatedDate, l.UpdatedBy,
                l.LineNumber, l.IsNonStandard, l.ProductId, l.CustomerProductId,
                l.Sku, l.LinkSku, l.CustomerSku, l.DescriptionEn, l.ColorNameEn, l.FeaturesEn,
                l.Qty, l.UnitSnapshot, l.CnyPrice, l.UsdPrice, l.PcsOfCarton, l.NwOfCarton, l.GwOfCarton,
                l.CbmOfCarton, l.ExpectedDeliveryDate, l.SubNote, l.MainImage,
                facts.ShippedQty, facts.UnshippedQty, facts.ReviewReason,
                ISNULL(l.CnyAmount, l.CnyPrice * l.Qty) AS CnyAmount,
                ISNULL(l.UsdAmount, l.UsdPrice * l.Qty) AS UsdAmount
            FROM dbo.SalesOrderLine l
            INNER JOIN #SalesLineFacts facts ON facts.SalesOrderLineId=l.Uid
            WHERE l.SalesOrderId = @Uid AND l.IsDeleted = 0
            ORDER BY l.LineNumber, l.Uid;";

        var command = new CommandDefinition(
            sql,
            new
            {
                Uid = uid, SalesSourceType = DocumentTypeStaticsEnum.SalesOrder,
                OrderOpen = SalesOrderDocumentStatusEnum.Open,
                OrderClosed = SalesOrderDocumentStatusEnum.Closed,
                Pending = ConfirmationStatusEnum.待确认,
                Confirmed = ConfirmationStatusEnum.已确认,
                DemandActive = GrossDemandLifecycleStatusEnum.Active,
                DemandVoided = GrossDemandLifecycleStatusEnum.Voided,
                Unposted = PackingListPostingStatusEnum.Unposted,
                Posted = PackingListPostingStatusEnum.Posted,
                Reversed = PackingListPostingStatusEnum.Reversed
            },
            transaction: Transaction,
            cancellationToken: cancellationToken);
        using var multi = await Connection.QueryMultipleAsync(command);
        var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeader>();
        if (header is null) return null;

        return new SalesOrderDetailDto
        {
            Header = MapHeader(header),
            Lines = (await multi.ReadAsync<SalesOrderDetailLineDto>()).ToList(),
            CloseReason = header.CloseReason ?? string.Empty,
            ClosedDate = header.ClosedDate,
            ClosedBy = header.ClosedBy ?? string.Empty
        };
    }

    private static SalesOrderEditHeaderDto MapHeader(SalesOrderHeader header) => new()
    {
        Uid = header.Uid,
        Version = header.Version,
        DocumentStatus = header.DocumentStatus,
        ConfirmationStatus = header.ConfirmationStatus,
        CustomerId = header.CustomerId ?? Guid.Empty,
        CustomerCode = header.CustomerCode ?? string.Empty,
        OrderNo = header.OrderNo,
        InvoiceNo = header.InvoiceNo ?? string.Empty,
        CustomerPoNo = header.CustomerPoNo ?? string.Empty,
        ShippedBy = header.ShippedBy ?? string.Empty,
        Currency = header.Currency ?? string.Empty,
        ExchangeRate = header.ExchangeRate ?? 0,
        ShipToName = header.ShipToName ?? string.Empty,
        ShipToAddress = header.ShipToAddress ?? string.Empty,
        ShipToPhone = header.ShipToPhone ?? string.Empty,
        Incoterm = header.Incoterm ?? string.Empty,
        IncotermSortRemark = header.IncotermSortRemark ?? string.Empty,
        IncotermLocation = header.IncotermLocation ?? string.Empty,
        DepositRate = header.DepositRate ?? 0,
        LeadTimeDays = header.LeadTimeDays,
        PaymentMethod = header.PaymentMethod ?? string.Empty,
        SoFolder = header.SoFolder ?? string.Empty,
        Remark = header.Remark ?? string.Empty,
        OrderDate = header.OrderDate,
        DeliveryDate = header.DeliveryDate,
        ReplyDate = header.ReplyDate,
        Eta = header.ETA,
        Etd = header.ETD
    };

    private static void AddLikeFilter(StringBuilder where, DynamicParameters parameters, string column, string parameterName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        where.Append($" AND {column} LIKE @{parameterName} ESCAPE '~'");
        parameters.Add(parameterName, SqlLikePattern.Build(value));
    }

    private static void AddDateRange(
        StringBuilder where,
        DynamicParameters parameters,
        string column,
        string parameterPrefix,
        DateTime? from,
        DateTime? to)
    {
        if (from.HasValue)
        {
            where.Append($" AND {column} >= @{parameterPrefix}From");
            parameters.Add($"{parameterPrefix}From", from.Value.Date);
        }
        if (to.HasValue)
        {
            where.Append($" AND {column} < @{parameterPrefix}To");
            parameters.Add($"{parameterPrefix}To", to.Value.Date.AddDays(1));
        }
    }
}
