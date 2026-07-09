using Dapper;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Customers;

public class CustomerPoConfigRepository(RepositoryBaseContext context) : DapperRepository<CustomerPoConfig>(context), ICustomerPoConfigRepository
{
    public async Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<PoConfigCheck> items)
    {
        var sql = $@"SELECT {nameof(CustomerPoConfig.CustomerCode)},
                            {nameof(CustomerPoConfig.SupplierCode)} 
                    FROM [{CustomerPoConfigTable}] 
                    WHERE  {nameof(Customer.IsDeleted)}=0 
                      AND {nameof(CustomerPoConfig.CustomerCode)} IN @CustomerCode
                      AND  {nameof(CustomerPoConfig.SupplierCode)} IN @SupplierCode";
        var rst = await Connection.QueryAsync<string>(sql, new
        {
            CustomerCode = items.Select(i => i.CustomerCode),
            SupplierCode = items.Select(i => i.SupplierCode)
        }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<IReadOnlyList<PoConfigCheck>> GetNonUniqueByPoConfigCheckList(IEnumerable<PoConfigCheck> poConfigCheckList)
    {
        // 防御性编程：如果没有数据，直接返回
        if (poConfigCheckList == null || !poConfigCheckList.Any())
        {
            return [];
        }
        var customerCodes = poConfigCheckList.Select(x => x.CustomerCode).Distinct().ToArray();
        var supplierCodes = poConfigCheckList.Select(x => x.SupplierCode).Distinct().ToArray();
        var sql = $@"SELECT CustomerCode, SupplierCode 
                    FROM [{TableName}] 
                    WHERE  CustomerCode IN @CustomerCodes AND SupplierCode IN @SupplierCodes";


        var potentialConflicts = await Connection.QueryAsync<PoConfigCheck>(sql,
            new { CustomerCodes = customerCodes, SupplierCodes = supplierCodes }, transaction: Transaction);

        var conflictList = potentialConflicts.AsList(); // 转换为 List 避免多次枚举
        if (conflictList.Count == 0)
        {
            // 连嫌疑犯都没有，绝对安全
            return [];
        }

        // ==========================================
        // 第四步：内存极速精准锁定（O(1) 哈希表匹配）
        // ==========================================
        var inputSet = new HashSet<string>(
            poConfigCheckList.Select(x => $"{x.CustomerCode}|{x.SupplierCode}")
        );

        var actualDuplicates = conflictList
            .Where(x => inputSet.Contains($"{x.CustomerCode}|{x.SupplierCode}"))
            .ToList();

        return actualDuplicates;

    }

    public async Task<PagedResponseBase<CustomerPoConfigDto>> GetPagedAsync(CustomerPoConfigSearchRequest request)
    {
        request ??= new CustomerPoConfigSearchRequest();

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        // 防止前端一次请求太多
        if (pageSize > 500)
            pageSize = 500;

        var offset = (pageIndex - 1) * pageSize;

        var parameters = new DynamicParameters();

        var whereSb = new StringBuilder();
        whereSb.AppendLine(@$"WHERE ISNULL(cpc.{nameof(CustomerPoConfig.IsDeleted)}, 0) = 0");

        // =========================================================
        // 1. 普通模糊关键词
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            whereSb.AppendLine(@$"
                                    AND (
                                           {nameof(CustomerPoConfig.CustomerCode)}       LIKE @Keyword
                                        OR {nameof(CustomerPoConfig.SupplierCode)}       LIKE @Keyword
                                    )");

            parameters.Add("@Keyword", $"%{keyword}%");
        }

        var whereClause = whereSb.ToString();

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var sql = $@"
                        SELECT COUNT(1)
                        FROM
                        {CustomerPoConfigTable}
                        {whereClause};
                        SELECT *
                        FROM
                        {CustomerPoConfigTable}
                        {whereClause}
                        ORDER BY {nameof(CustomerPoConfig.CreatedDate)} DESC
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                        ";

        using var multi = await Connection.QueryMultipleAsync(
            sql,
            parameters,
            transaction: Transaction);

        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<CustomerPoConfigDto>();

        return new PagedResponseBase<CustomerPoConfigDto>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
    public async Task<List<string>> GetUsedByCustomerCodeAndSupplierCodeAsync(IEnumerable<PoConfigCheck> items)
    {
        var list = items
            .Where(x =>
                x.Uid != Guid.Empty &&
                !string.IsNullOrWhiteSpace(x.CustomerCode) &&
                !string.IsNullOrWhiteSpace(x.SupplierCode))
            .Select(x => new PoConfigCheck
            {
                Uid = x.Uid,
                CustomerCode = x.CustomerCode!.Trim(),
                SupplierCode = x.SupplierCode!.Trim()
            })
            .ToList();

        if (list.Count == 0)
        {
            return [];
        }

        var customerCodes = list
            .Select(x => x.CustomerCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var supplierCodes = list
            .Select(x => x.SupplierCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sql = $@"
                       SELECT 
                           {nameof(CustomerPoConfig.Uid)},
                           {nameof(CustomerPoConfig.CustomerCode)},
                           {nameof(CustomerPoConfig.SupplierCode)}
                       FROM {CustomerPoConfigTable}
                       WHERE {nameof(CustomerPoConfig.IsDeleted)} = 0
                         AND {nameof(CustomerPoConfig.CustomerCode)} IN @CustomerCodes
                         AND {nameof(CustomerPoConfig.SupplierCode)} IN @SupplierCodes;
                       ";

        var dbRows = (await Connection.QueryAsync<PoConfigCheck>(
            sql,
            new
            {
                CustomerCodes = customerCodes,
                SupplierCodes = supplierCodes
            },
             transaction: Transaction))
            .ToList();

        var usedPairs = new List<string>();

        foreach (var item in list)
        {
            var exists = dbRows.Any(db =>
                db.Uid != item.Uid &&
                string.Equals(db.CustomerCode, item.CustomerCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(db.SupplierCode, item.SupplierCode, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                usedPairs.Add($"{item.CustomerCode} + {item.SupplierCode}");
            }
        }

        return [.. usedPairs.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}

