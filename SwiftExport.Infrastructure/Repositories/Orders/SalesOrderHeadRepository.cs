using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public class SalesOrderHeadRepository(RepositoryBaseContext context)
    : DapperRepository<SalesOrderHeader>(context), ISalesOrderHeadRepository
{
    public async Task<PagedResponseBase<SalesOrderListItemDto>> SearchWorkspaceAsync(
        SalesOrderSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new DynamicParameters(new
        {
            SourceType = DocumentTypeStaticsEnum.SalesOrder,
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
                        OR keywordLine.DefaultDescriptionEn LIKE @Keyword ESCAPE '~')))");
        }

        if (request.FulfillmentStatuses?.Count > 0)
        {
            where.Append(" AND totals.FulfillmentStatus IN @FulfillmentStatuses");
            parameters.Add("FulfillmentStatuses", request.FulfillmentStatuses.Distinct().ToArray());
        }

        var fromSql = @"
            FROM SalesOrderHeader h WITH(NOLOCK)
            LEFT JOIN Customer c WITH(NOLOCK)
              ON c.Uid = h.CustomerId AND c.IsDeleted = 0
            OUTER APPLY (
                SELECT
                    ISNULL(SUM(lineTotals.Qty), 0) AS TotalQty,
                    ISNULL(SUM(lineTotals.ShippedQty), 0) AS TotalShippedQty,
                    CASE
                        WHEN ISNULL(SUM(lineTotals.ShippedQty), 0) <= 0 THEN 0
                        WHEN ISNULL(SUM(lineTotals.Qty), 0) > 0
                         AND ISNULL(SUM(lineTotals.ShippedQty), 0) >= ISNULL(SUM(lineTotals.Qty), 0) THEN 2
                        ELSE 1
                    END AS FulfillmentStatus
                FROM (
                    SELECT l.Qty,
                           ISNULL((SELECT SUM(outLine.Quantity)
                                   FROM dbo.OutboundDocumentLine outLine WITH(NOLOCK)
                                   JOIN dbo.OutboundDocument outHead WITH(NOLOCK) ON outHead.Uid=outLine.OutboundDocumentId
                                   JOIN dbo.NetDemandAllocation allocation WITH(NOLOCK) ON allocation.Uid=outLine.NetDemandAllocationId
                                   JOIN dbo.NetDemand netDemand WITH(NOLOCK) ON netDemand.Uid=allocation.NetDemandId
                                   JOIN dbo.GrossDemand demand WITH(NOLOCK) ON demand.Uid=netDemand.GrossDemandId
                                   WHERE demand.SourceType=@SourceType AND demand.SourceLineId=l.Uid
                                     AND outHead.DocumentType=20 AND outHead.PostingStatus=1
                                     AND ISNULL(outHead.IsDeleted,0)=0 AND ISNULL(outLine.IsDeleted,0)=0), 0) AS ShippedQty
                    FROM SalesOrderLine l WITH(NOLOCK)
                    WHERE l.SalesOrderId = h.Uid AND l.IsDeleted = 0
                ) lineTotals
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
                totals.FulfillmentStatus,
                h.OrderNo,
                ISNULL(h.CustomerId, '00000000-0000-0000-0000-000000000000') AS CustomerId,
                ISNULL(h.CustomerCode, '') AS CustomerCode,
                ISNULL(c.CustomerName, '') AS CustomerName,
                ISNULL(h.CustomerPoNo, '') AS CustomerPoNo,
                ISNULL(h.InvoiceNo, '') AS InvoiceNo,
                ISNULL(h.Currency, '') AS Currency,
                ISNULL(h.ExchangeRate, 0) AS ExchangeRate,
                totals.TotalQty,
                totals.TotalShippedQty,
                CASE WHEN totals.TotalQty > totals.TotalShippedQty
                     THEN totals.TotalQty - totals.TotalShippedQty ELSE 0 END AS TotalUnshippedQty,
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
        var items = (await multi.ReadAsync<SalesOrderListItemDto>()).ToList();
        return new PagedResponseBase<SalesOrderListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<SalesOrderDocumentDto?> GetWorkspaceDocumentAsync(
        Guid uid,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM SalesOrderHeader WITH(NOLOCK)
            WHERE Uid = @Uid AND IsDeleted = 0;

            SELECT
                l.Uid, l.Version, l.CreatedDate, l.CreatedBy, l.UpdatedDate, l.UpdatedBy,
                l.LineNumber, l.IsNonStandard, l.ProductId, l.CustomerProductId,
                l.Sku, l.LinkSku, l.CustomerSku, l.DefaultDescriptionEn, l.ColorNameEn, l.FeaturesEn,
                l.Qty, l.CnyPrice, l.UsdPrice, l.PcsOfCarton, l.NwOfCarton, l.GwOfCarton,
                l.CbmOfCarton, l.ExpectedDeliveryDate, l.SubNote, l.MainImage,
                shipped.ShippedQty,
                CASE WHEN l.Qty > shipped.ShippedQty THEN l.Qty - shipped.ShippedQty ELSE 0 END AS UnshippedQty,
                ISNULL(l.CnyAmount, l.CnyPrice * l.Qty) AS CnyAmount,
                ISNULL(l.UsdAmount, l.UsdPrice * l.Qty) AS UsdAmount
            FROM SalesOrderLine l WITH(NOLOCK)
            OUTER APPLY (
                SELECT ISNULL(SUM(outLine.Quantity),0) AS ShippedQty
                FROM dbo.OutboundDocumentLine outLine WITH(NOLOCK)
                JOIN dbo.OutboundDocument outHead WITH(NOLOCK) ON outHead.Uid=outLine.OutboundDocumentId
                JOIN dbo.NetDemandAllocation allocation WITH(NOLOCK) ON allocation.Uid=outLine.NetDemandAllocationId
                JOIN dbo.NetDemand netDemand WITH(NOLOCK) ON netDemand.Uid=allocation.NetDemandId
                JOIN dbo.GrossDemand demand WITH(NOLOCK) ON demand.Uid=netDemand.GrossDemandId
                WHERE demand.SourceType=@SourceType AND demand.SourceLineId=l.Uid
                  AND outHead.DocumentType=20 AND outHead.PostingStatus=1
                  AND ISNULL(outHead.IsDeleted,0)=0 AND ISNULL(outLine.IsDeleted,0)=0
            ) shipped
            WHERE l.SalesOrderId = @Uid AND l.IsDeleted = 0
            ORDER BY l.LineNumber, l.Uid;";

        var command = new CommandDefinition(
            sql,
            new
            {
                Uid = uid,
                SourceType = DocumentTypeStaticsEnum.SalesOrder
            },
            transaction: Transaction,
            cancellationToken: cancellationToken);
        using var multi = await Connection.QueryMultipleAsync(command);
        var header = await multi.ReadSingleOrDefaultAsync<SalesOrderHeader>();
        if (header is null) return null;

        return new SalesOrderDocumentDto
        {
            Header = MapHeader(header),
            Lines = (await multi.ReadAsync<SalesOrderDocumentLineDto>()).ToList(),
            CloseReason = header.CloseReason ?? string.Empty,
            ClosedDate = header.ClosedDate,
            ClosedBy = header.ClosedBy ?? string.Empty
        };
    }

    public Task<RepositoryWriteResult> SetConfirmationStatusAsync(Guid uid, int version, ConfirmationStatusEnum status)
        => UpdateStatusAsync(uid, version, "ConfirmationStatus = @Status", new { Status = status });

    public async Task<RepositoryWriteResult> CloseWorkspaceAsync(Guid uid, int version, string reason)
    {
        var now = DateTime.Now;
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
            UPDATE SalesOrderHeader
            SET DocumentStatus = @ClosedStatus,
                CloseReason = @Reason,
                ClosedDate = @Now,
                ClosedBy = @UserName,
                UpdatedDate = @Now,
                UpdatedBy = @UserName,
                Version = Version + 1
            OUTPUT INSERTED.Version
            WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0 AND DocumentStatus = @OpenStatus;",
            new
            {
                Uid = uid,
                Version = version,
                Reason = reason,
                Now = now,
                UserName = CurrentUser.UserName,
                OpenStatus = SalesOrderDocumentStatusEnum.Open,
                ClosedStatus = SalesOrderDocumentStatusEnum.Closed
            }, transaction: Transaction);
        return ToWriteResult(uid, version, newVersion, "关闭销售订单");
    }

    public async Task<RepositoryWriteResult> DeleteDraftAsync(Guid uid, int version)
    {
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
            UPDATE SalesOrderHeader
            SET IsDeleted = 1,
                UpdatedDate = @Now,
                UpdatedBy = @UserName,
                Version = Version + 1
            OUTPUT INSERTED.Version
            WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0
              AND DocumentStatus = @OpenStatus AND ConfirmationStatus = @PendingStatus;",
            new
            {
                Uid = uid,
                Version = version,
                Now = DateTime.Now,
                UserName = CurrentUser.UserName,
                OpenStatus = SalesOrderDocumentStatusEnum.Open,
                PendingStatus = ConfirmationStatusEnum.待确认
            }, transaction: Transaction);
        return ToWriteResult(uid, version, newVersion, "删除销售订单草稿");
    }

    private async Task<RepositoryWriteResult> UpdateStatusAsync(Guid uid, int version, string statusSet, object statusParameter)
    {
        var parameters = new DynamicParameters(statusParameter);
        parameters.Add("Uid", uid);
        parameters.Add("Version", version);
        parameters.Add("Now", DateTime.Now);
        parameters.Add("UserName", CurrentUser.UserName);
        var sql = $@"
            UPDATE SalesOrderHeader
            SET {statusSet}, UpdatedDate = @Now, UpdatedBy = @UserName, Version = Version + 1
            OUTPUT INSERTED.Version
            WHERE Uid = @Uid AND Version = @Version AND IsDeleted = 0;";
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(sql, parameters, transaction: Transaction);
        return ToWriteResult(uid, version, newVersion, "更新销售订单状态");
    }

    private static RepositoryWriteResult ToWriteResult(Guid uid, int version, int? newVersion, string operation)
        => newVersion.HasValue
            ? RepositoryWriteResult.Ok(uid, version, newVersion.Value, $"{operation}成功。")
            : RepositoryWriteResult.Fail(uid, version, "CONCURRENCY_CONFLICT", $"{operation}失败，订单已被其他用户修改，请刷新后重试。");

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
