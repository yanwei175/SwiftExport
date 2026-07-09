using Dapper;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Customers;

public class CustomerRepository(RepositoryBaseContext context) : DapperRepository<Customer>(context), ICustomerRepository
{

    public async Task<bool> ExistByCustomerCode(string customerCode)
    {
        var sql = $"SELECT 1 FROM [{TableName}] WHERE CustomerCode = @CustomerCode";
        int? result = await Connection.QueryFirstOrDefaultAsync<int?>(sql, new { CustomerCode = customerCode }, transaction: Transaction);
        // 如果 result 有值（即不是 null），则表示存在。
        return result.HasValue;
    }

    public async Task<Customer?> GetByCustomerCodeAsync(string customerCode)
    {
        var sql = $"SELECT * FROM [{CustomerTable}] WHERE  CustomerCode = @CustomerCode AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QueryFirstOrDefaultAsync<Customer>(sql, new { CustomerCode = customerCode }, transaction: Transaction);
    }

    public async Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items)
    {
        var sql = $@"
            SELECT {nameof(Customer.CustomerCode)}
            FROM [{CustomerTable}]
            WHERE {nameof(Customer.CustomerCode)} IN @Codes AND {nameof(Customer.Uid)} NOT IN @Uids AND {nameof(Customer.IsDeleted)}=0";

        var result = await Connection.QueryAsync<string>(sql,
            new
            {
                Codes = items.Select(i => i.CustomerCode),
                Uids = items.Select(i => i.Uid)
            },
                transaction: Transaction);

        return [.. result];
    }

    public async Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<string> customerCodes)
    {
        var sql = $"SELECT CustomerCode FROM [{TableName}] WHERE  CustomerCode IN @Codes";
        var rst = await Connection.QueryAsync<string>(sql, new { Codes = customerCodes }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<PagedResponseBase<CustomerDto>> GetPagedAsync(CustomerSearchRequest request)
    {
        request ??= new CustomerSearchRequest();

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        // 防止前端一次请求太多
        if (pageSize > 500)
            pageSize = 500;

        var offset = (pageIndex - 1) * pageSize;

        var parameters = new DynamicParameters();

        var whereSb = new StringBuilder();
        whereSb.AppendLine(@$"WHERE ISNULL(cpc.{nameof(Customer.IsDeleted)}, 0) = 0");

        // =========================================================
        // 1. 普通模糊关键词
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            whereSb.AppendLine(@$"
                               AND (
                                       {nameof(Customer.CustomerCode)}       LIKE @Keyword
                                    OR {nameof(Customer.CustomerName)}       LIKE @Keyword
                                    )");

            parameters.Add("@Keyword", $"%{keyword}%");
        }

        var whereClause = whereSb.ToString();

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var sql = $@"
                        SELECT COUNT(1)
                        FROM
                        {CustomerTable}
                        {whereClause};
                        SELECT *
                        FROM
                        {CustomerTable}
                        {whereClause}
                        ORDER BY {nameof(Customer.CustomerCode)} DESC
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                        ";

        using var multi = await Connection.QueryMultipleAsync(
                                            sql,
                                            parameters,
                                            transaction: Transaction);

        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<CustomerDto>();

        return new PagedResponseBase<CustomerDto>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }


}
