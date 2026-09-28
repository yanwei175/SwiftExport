using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

using SwiftExport.Core.Interfaces.SalesOrders.Query;
namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>使用请求级连接查询销售订单列表和详情，保持原筛选、排序和投影 SQL。</summary>
public sealed class SalesOrderQueryRepository(RepositoryBaseContext context) : DapperRepository<SalesOrderHeader>(context), ISalesOrderQueryRepository
{
    /// <summary>按 Manager 筛选条件分页查询销售订单摘要。</summary>
    public async Task<PagedResponseBase<SalesOrderManagerListItemDto>> SearchManagerAsync(
        SalesOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize
        });
        var where = new StringBuilder("WHERE h.IsDeleted = 0");

        var customerCodes = request.CustomerCodes?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (customerCodes.Length > 0)
        {
            where.Append(" AND h.CustomerCode IN @CustomerCodes");
            parameters.Add("CustomerCodes", customerCodes);
        }
        AddLikeFilter(where, parameters, "h.OrderNo", "OrderNo", request.OrderNo);
        AddLikeFilter(where, parameters, "h.CustomerPoNo", "CustomerPoNo", request.CustomerPoNo);
        AddLikeFilter(where, parameters, "h.InvoiceNo", "InvoiceNo", request.InvoiceNo);

        if (request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty)
        {
            where.Append(" AND h.CustomerId = @CustomerId");
            parameters.Add("CustomerId", request.CustomerId.Value);
        }

        AddDateRange(where, parameters, "h.OrderDate", "OrderDate", request.OrderDateFrom, request.OrderDateTo);
        AddDateRange(where, parameters, "h.DeliveryDate", "DeliveryDate", request.DeliveryDateFrom, request.DeliveryDateTo);

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

        var customerSkus = request.CustomerSkus?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (customerSkus.Length > 0)
        {
            where.Append(@" AND EXISTS (
                SELECT 1 FROM SalesOrderLine skuLine WITH(NOLOCK)
                WHERE skuLine.SalesOrderId = h.Uid
                  AND skuLine.IsDeleted = 0
                  AND skuLine.CustomerSku IN @CustomerSkus)");
            parameters.Add("CustomerSkus", customerSkus);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where.Append(@" AND (
                h.OrderNo LIKE @Keyword ESCAPE '~'
                OR h.CustomerCode LIKE @Keyword ESCAPE '~'
                OR h.CustomerPoNo LIKE @Keyword ESCAPE '~'
                OR h.InvoiceNo LIKE @Keyword ESCAPE '~'
                OR c.CustomerName LIKE @Keyword ESCAPE '~'
                OR EXISTS (
                    SELECT 1 FROM SalesOrderLine keywordLine WITH(NOLOCK)
                    WHERE keywordLine.SalesOrderId = h.Uid
                      AND keywordLine.IsDeleted = 0
                      AND (keywordLine.Sku LIKE @Keyword ESCAPE '~'
                        OR keywordLine.CustomerSku LIKE @Keyword ESCAPE '~'
                        OR keywordLine.LinkSku LIKE @Keyword ESCAPE '~'
                        OR keywordLine.DescriptionEn LIKE @Keyword ESCAPE '~')))");
        }

        var fromSql = @"
            FROM SalesOrderHeader h WITH(NOLOCK)
            LEFT JOIN Customer c WITH(NOLOCK)
              ON c.Uid = h.CustomerId AND c.IsDeleted = 0
            OUTER APPLY (
                SELECT ISNULL(SUM(l.Qty), 0) AS TotalQty
                FROM SalesOrderLine l WITH(NOLOCK)
                WHERE l.SalesOrderId = h.Uid AND l.IsDeleted = 0
            ) totals";

        var sql = $@"
            SELECT COUNT(1)
            {fromSql}
            {where};

            SELECT
                h.Uid,
                h.Version,
                h.DocumentStatus,
                h.ConfirmationStatus,
                CAST(NULL AS int) AS FulfillmentStatus,
                h.OrderNo,
                ISNULL(h.CustomerId, '00000000-0000-0000-0000-000000000000') AS CustomerId,
                ISNULL(h.CustomerCode, '') AS CustomerCode,
                ISNULL(c.CustomerName, '') AS CustomerName,
                ISNULL(h.CustomerPoNo, '') AS CustomerPoNo,
                ISNULL(h.InvoiceNo, '') AS InvoiceNo,
                ISNULL(h.Currency, '') AS Currency,
                ISNULL(h.ExchangeRate, 0) AS ExchangeRate,
                totals.TotalQty,
                CAST(NULL AS decimal(18, 4)) AS TotalShippedQty,
                CAST(NULL AS decimal(18, 4)) AS TotalUnshippedQty,
                ISNULL(h.TotalAmountOriginalCurrency, 0) AS TotalAmountOriginalCurrency,
                ISNULL(h.TotalAmountLocalCurrency, 0) AS TotalAmountLocalCurrency,
                h.OrderDate,
                h.DeliveryDate,
                h.ReplyDate,
                h.CreatedDate,
                ISNULL(h.CreatedBy, '') AS CreatedBy,
                h.UpdatedDate,
                ISNULL(h.UpdatedBy, '') AS UpdatedBy
            {fromSql}
            {where}
            ORDER BY h.CreatedDate DESC, h.Uid DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        var command = new CommandDefinition(
            sql,
            parameters,
            transaction: Transaction,
            cancellationToken: cancellationToken);
        using var multi = await Connection.QueryMultipleAsync(command);
        var totalCount = await multi.ReadFirstAsync<int>();
        var items = (await multi.ReadAsync<SalesOrderManagerListItemDto>()).ToList();
        return new PagedResponseBase<SalesOrderManagerListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// 读取销售订单头与有效行组成的 Detail；PackingList 净交付尚未接入前，交付数量显式返回未知。
    /// </summary>
    public async Task<SalesOrderDetailDto?> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM SalesOrderHeader WITH(NOLOCK)
            WHERE Uid = @Uid AND IsDeleted = 0;

            SELECT
                l.Uid, l.Version, l.CreatedDate, l.CreatedBy, l.UpdatedDate, l.UpdatedBy,
                l.LineNumber, l.IsNonStandard, l.ProductId, l.CustomerProductId,
                l.Sku, l.LinkSku, l.CustomerSku, l.DescriptionEn, l.ColorNameEn, l.FeaturesEn,
                l.Qty, l.UnitSnapshot, l.CnyPrice, l.UsdPrice, l.PcsOfCarton, l.NwOfCarton, l.GwOfCarton,
                l.CbmOfCarton, l.ExpectedDeliveryDate, l.SubNote, l.MainImage,
                CAST(NULL AS decimal(18, 4)) AS ShippedQty,
                CAST(NULL AS decimal(18, 4)) AS UnshippedQty,
                ISNULL(l.CnyAmount, l.CnyPrice * l.Qty) AS CnyAmount,
                ISNULL(l.UsdAmount, l.UsdPrice * l.Qty) AS UsdAmount
            FROM SalesOrderLine l WITH(NOLOCK)
            WHERE l.SalesOrderId = @Uid AND l.IsDeleted = 0
            ORDER BY l.LineNumber, l.Uid;";

        var command = new CommandDefinition(
            sql,
            new { Uid = uid },
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
