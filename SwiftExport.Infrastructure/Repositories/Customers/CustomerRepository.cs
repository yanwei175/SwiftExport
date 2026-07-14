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
        var sql = $"SELECT * FROM [{TableName}] WHERE  CustomerCode = @CustomerCode AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QueryFirstOrDefaultAsync<Customer>(sql, new { CustomerCode = customerCode }, transaction: Transaction);
    }

    public async Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items)
    {
        var sql = $@"
            SELECT {nameof(Customer.CustomerCode)}
            FROM [{TableName}]
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
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);

        var offset = (pageIndex - 1) * pageSize;

        var parameters = new DynamicParameters();

        var whereSb = new StringBuilder();
        whereSb.AppendLine("WHERE ISNULL(c.IsDeleted, 0) = 0");

        // =========================================================
        // 1. 普通模糊关键词
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            whereSb.AppendLine(@$"
                               AND (
                                       c.CustomerCode LIKE @Keyword
                                    OR c.CustomerName LIKE @Keyword
                                    )");

            parameters.Add("@Keyword", $"%{keyword}%");
        }

        var whereClause = whereSb.ToString();

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var sql = $@"
                        SELECT COUNT(1)
                        FROM dbo.[Customer] c
                        {whereClause};
                        SELECT
                            c.Uid, c.Version, c.IsDeleted,
                            c.CreatedDate, c.CreatedBy, c.UpdatedDate, c.UpdatedBy,
                            c.CustomerCode, c.CustomerName, c.Address, c.PhoneNumber,
                            c.ContactPerson, c.LabelFolderPath, c.ManualFolderPath,
                            c.DrawingFolderPath, c.LinedrawingFolderPath,
                            c.BarcodeFolderPath, c.SpecSheetFolderPath, c.DocRelateNo,
                            c.IncotermSortRemark, c.Incoterm, c.IncotermLocation,
                            c.DepositRate, c.LeadTimeDays, c.PaymentMethod, c.Currency,
                            c.EtdSortRemark, c.ShippedBy
                        FROM dbo.[Customer] c
                        {whereClause}
                        ORDER BY c.CustomerCode ASC, c.Uid ASC
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
