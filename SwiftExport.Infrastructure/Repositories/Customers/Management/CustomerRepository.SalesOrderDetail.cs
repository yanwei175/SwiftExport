using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Customers.Management;

/// <summary>提供销售订单 Detail 所需的客户 Lookup 查询。</summary>
public partial class CustomerRepository
{
    /// <summary>按关键词分页查询可用于销售订单的有效客户及其默认商业信息。</summary>
    public async Task<PagedResponseBase<SalesOrderCustomerLookupDto>> SearchSalesOrderCustomersAsync(
        SalesOrderCustomerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 30 : request.PageSize, 100);
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize
        });
        var keywordSql = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            keywordSql = @" AND (c.CustomerCode LIKE @Keyword ESCAPE '~'
                               OR c.CustomerName LIKE @Keyword ESCAPE '~'
                               OR c.ContactPerson LIKE @Keyword ESCAPE '~'
                               OR c.PhoneNumber LIKE @Keyword ESCAPE '~')";
        }

        var sql = $@"
            SELECT COUNT(1)
            FROM Customer c WITH(NOLOCK)
            WHERE c.IsDeleted = 0 {keywordSql};

            SELECT
                c.Uid AS CustomerId,
                ISNULL(c.CustomerCode, '') AS CustomerCode,
                ISNULL(c.CustomerName, '') AS CustomerName,
                ISNULL(c.Currency, '') AS Currency,
                ISNULL(c.PaymentMethod, '') AS PaymentMethod,
                ISNULL(c.ShippedBy, '') AS ShippedBy,
                ISNULL(c.Incoterm, '') AS Incoterm,
                ISNULL(c.IncotermSortRemark, '') AS IncotermSortRemark,
                ISNULL(c.IncotermLocation, '') AS IncotermLocation,
                ISNULL(c.DepositRate, 0) AS DepositRate,
                ISNULL(c.LeadTimeDays, 0) AS LeadTimeDays,
                ISNULL(c.CustomerName, '') AS ShipToName,
                ISNULL(c.Address, '') AS ShipToAddress,
                ISNULL(c.PhoneNumber, '') AS ShipToPhone
            FROM Customer c WITH(NOLOCK)
            WHERE c.IsDeleted = 0 {keywordSql}
            ORDER BY c.CustomerCode, c.Uid
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var command = new CommandDefinition(
            sql,
            parameters,
            transaction: Transaction,
            cancellationToken: cancellationToken);
        using var multi = await Connection.QueryMultipleAsync(command);
        return new PagedResponseBase<SalesOrderCustomerLookupDto>
        {
            TotalCount = await multi.ReadFirstAsync<int>(),
            Items = (await multi.ReadAsync<SalesOrderCustomerLookupDto>()).ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<List<string>> GetSalesOrderCustomerCodesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT DISTINCT LTRIM(RTRIM(c.CustomerCode)) AS CustomerCode
            FROM Customer c WITH(NOLOCK)
            WHERE c.IsDeleted = 0
              AND NULLIF(LTRIM(RTRIM(c.CustomerCode)), '') IS NOT NULL
            ORDER BY CustomerCode;";
        var command = new CommandDefinition(
            sql,
            transaction: Transaction,
            cancellationToken: cancellationToken);
        return (await Connection.QueryAsync<string>(command)).ToList();
    }
}
