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

/// <summary>提供客户采购默认配置的分页查询、关系解析和组合唯一性检查。</summary>
public class CustomerPoConfigRepository(RepositoryBaseContext context) : DapperRepository<CustomerPoConfig>(context), ICustomerPoConfigRepository
{
    /// <summary>按客户与供应商标识读取完整的活动配置 DTO。</summary>
    public Task<CustomerPoConfigDto?> GetByPartiesAsync(
        Guid customerId,
        Guid supplierId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                cpc.Uid, cpc.Version, cpc.IsDeleted,
                cpc.CreatedDate, cpc.CreatedBy, cpc.UpdatedDate, cpc.UpdatedBy,
                cpc.CustomerId, cpc.CustomerCode, cpc.SupplierId, cpc.SupplierCode,
                cpc.Currency, cpc.DefaultDiscountRate, cpc.DiscountRemark,
                cpc.InletHoseType, cpc.AeratorType, cpc.PalletType, cpc.BoxType,
                cpc.LabelType, cpc.IMType, cpc.CartonType, cpc.LogoType,
                cpc.SalesPerson, cpc.Purchaser, cpc.Note
            FROM dbo.CustomerPoConfig AS cpc
            WHERE cpc.CustomerId = @CustomerId
              AND cpc.SupplierId = @SupplierId
              AND cpc.IsDeleted = 0;
            """;

        return Connection.QuerySingleOrDefaultAsync<CustomerPoConfigDto>(new CommandDefinition(
            sql,
            new { CustomerId = customerId, SupplierId = supplierId },
            Transaction,
            cancellationToken: cancellationToken));
    }

    /// <summary>按唯一有效业务编码解析客户与供应商标识并写入实体。</summary>
    public async Task<bool> PopulateRelationIdsAsync(CustomerPoConfig entity)
    {
        const string sql = """
            SELECT
              (SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(Uid) END FROM dbo.Customer
               WHERE ISNULL(IsDeleted,0)=0
                 AND ((@CustomerId IS NOT NULL AND Uid=@CustomerId) OR (@CustomerId IS NULL AND CustomerCode=@CustomerCode))) AS CustomerId,
              (SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(CustomerCode) END FROM dbo.Customer
               WHERE ISNULL(IsDeleted,0)=0
                 AND ((@CustomerId IS NOT NULL AND Uid=@CustomerId) OR (@CustomerId IS NULL AND CustomerCode=@CustomerCode))) AS CustomerCode,
              (SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(Uid) END FROM dbo.Supplier
               WHERE ISNULL(IsDeleted,0)=0
                 AND ((@SupplierId IS NOT NULL AND Uid=@SupplierId) OR (@SupplierId IS NULL AND SupplierCode=@SupplierCode))) AS SupplierId,
              (SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(SupplierCode) END FROM dbo.Supplier
               WHERE ISNULL(IsDeleted,0)=0
                 AND ((@SupplierId IS NOT NULL AND Uid=@SupplierId) OR (@SupplierId IS NULL AND SupplierCode=@SupplierCode))) AS SupplierCode;
            """;
        var ids = await Connection.QuerySingleAsync<RelationIds>(sql, new
        {
            entity.CustomerId,
            entity.CustomerCode,
            entity.SupplierId,
            entity.SupplierCode
        }, Transaction);
        if (!ids.CustomerId.HasValue || !ids.SupplierId.HasValue) return false;
        entity.CustomerId = ids.CustomerId;
        entity.CustomerCode = ids.CustomerCode;
        entity.SupplierId = ids.SupplierId;
        entity.SupplierCode = ids.SupplierCode;
        return true;
    }

    /// <summary>返回活动记录中已经存在的客户与供应商完整组合。</summary>
    public async Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<PoConfigCheck> items)
    {
        var sql = $@"SELECT {nameof(CustomerPoConfig.CustomerCode)},
                            {nameof(CustomerPoConfig.SupplierCode)}
                    FROM [{TableName}]
                    WHERE  {nameof(Customer.IsDeleted)}=0
                      AND {nameof(CustomerPoConfig.CustomerCode)} IN @CustomerCode
                      AND  {nameof(CustomerPoConfig.SupplierCode)} IN @SupplierCode";
        var candidates = (await Connection.QueryAsync<PoConfigCheck>(sql, new
        {
            CustomerCode = items.Select(i => i.CustomerCode),
            SupplierCode = items.Select(i => i.SupplierCode)
        }, transaction: Transaction)).ToList();
        var requested = items.Select(ToPairKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(item => requested.Contains(ToPairKey(item)))
            .Select(item => $"{item.CustomerCode} + {item.SupplierCode}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>按客户或供应商编码分页查询活动配置。</summary>
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
            whereSb.AppendLine(@$"
                                    AND (
                                           cpc.{nameof(CustomerPoConfig.CustomerCode)} LIKE @Keyword ESCAPE '~'
                                        OR cpc.{nameof(CustomerPoConfig.SupplierCode)} LIKE @Keyword ESCAPE '~'
                                    )");

            parameters.Add("@Keyword", SqlLikePattern.Build(request.Keyword));
        }

        var whereClause = whereSb.ToString();

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var sql = $@"
                        SELECT COUNT(1)
                        FROM dbo.[{TableName}] AS cpc
                        {whereClause};
                        SELECT
                            cpc.Uid, cpc.CustomerId, cpc.CustomerCode, cpc.SupplierId, cpc.SupplierCode,
                            cpc.Currency, cpc.DefaultDiscountRate, cpc.DiscountRemark,
                            cpc.InletHoseType, cpc.AeratorType, cpc.PalletType, cpc.BoxType,
                            cpc.LabelType, cpc.IMType, cpc.CartonType, cpc.LogoType,
                            cpc.SalesPerson, cpc.Purchaser, cpc.Note,
                            cpc.Version, cpc.IsDeleted,
                            cpc.CreatedDate, cpc.CreatedBy, cpc.UpdatedDate, cpc.UpdatedBy
                        FROM dbo.[{TableName}] AS cpc
                        {whereClause}
                        ORDER BY cpc.{nameof(CustomerPoConfig.CreatedDate)} DESC, cpc.{nameof(CustomerPoConfig.Uid)} DESC
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
    /// <summary>返回被其他活动记录占用的客户与供应商组合。</summary>
    public async Task<List<string>> GetUsedByCustomerCodeAndSupplierCodeAsync(IEnumerable<PoConfigCheck> items)
    {
        var list = items
            .Where(x =>
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
                       FROM {TableName}
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
                (item.Uid == Guid.Empty || db.Uid != item.Uid) &&
                string.Equals(db.CustomerCode, item.CustomerCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(db.SupplierCode, item.SupplierCode, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                usedPairs.Add($"{item.CustomerCode} + {item.SupplierCode}");
            }
        }

        return [.. usedPairs.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>建立忽略大小写的客户与供应商组合键。</summary>
    private static string ToPairKey(PoConfigCheck item) =>
        $"{item.CustomerCode?.Trim()}|{item.SupplierCode?.Trim()}";

    private sealed class RelationIds
    {
        public Guid? CustomerId { get; set; }
        public string? CustomerCode { get; set; }
        public Guid? SupplierId { get; set; }
        public string? SupplierCode { get; set; }
    }
}

