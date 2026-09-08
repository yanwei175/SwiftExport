using Dapper;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products;

public sealed class CustomerProductRepository(RepositoryBaseContext context)
    : DapperRepository<CustomerProduct>(context), ICustomerProductRepository
{
    private const string SelectColumns = """
        cp.Uid, cp.Version, cp.IsDeleted,
        cp.CreatedDate, cp.CreatedBy, cp.UpdatedDate, cp.UpdatedBy,
        cp.CustomerId, c.CustomerCode,
        cp.SkuId, cp.Sku, cp.LinkSku,
        COALESCE(p.DescriptionEn, cp.DescriptionEn) AS Description,
        cp.DescriptionEn, cp.FeaturesEn,
        CASE WHEN pm.Uid IS NULL OR pm.CategoryId IS NULL OR pm.CategoryId='00000000-0000-0000-0000-000000000000'
             THEN N'未分类' ELSE COALESCE(pc.CategoryName, N'分类已失效') END AS CategoryName,
        p.MaterialType, p.MainImage,
        cp.CustomerSku, cp.CustomerPdDescription, cp.CustomerPdSeries,
        cp.CustomerColorName, cp.CustomerPdFeatures,
        cp.CustomerBarcode1, cp.CustomerBarcode2,
        cp.USDPrice, cp.CNYPrice,
        cp.LeadTimeDays, cp.MOQ, cp.IsActive, cp.Note
        """;

    private const string FromAndJoins = """
        FROM dbo.CustomerProduct cp WITH (NOLOCK)
        INNER JOIN dbo.Customer c WITH (NOLOCK) ON c.Uid = cp.CustomerId
        LEFT JOIN dbo.Product p WITH (NOLOCK) ON p.Uid = cp.SkuId AND ISNULL(p.IsDeleted, 0) = 0
        LEFT JOIN dbo.ProductMaster pm WITH (NOLOCK) ON pm.Uid = p.ProductMasterId AND ISNULL(pm.IsDeleted, 0) = 0
        LEFT JOIN dbo.ProductCategory pc WITH (NOLOCK) ON pc.Uid = pm.CategoryId AND ISNULL(pc.IsDeleted, 0) = 0
        """;

    public async Task<CustomerProductDto?> GetDtoByIdAsync(Guid uid)
    {
        var sql = $"""
            SELECT {SelectColumns}
            {FromAndJoins}
            WHERE cp.Uid = @Uid
              AND ISNULL(cp.IsDeleted, 0) = 0
              AND ISNULL(c.IsDeleted, 0) = 0;
            """;
        return await Connection.QueryFirstOrDefaultAsync<CustomerProductDto>(
            sql, new { Uid = uid }, transaction: Transaction);
    }

    public async Task<PagedResponseBase<CustomerProductDto>> GetPagedAsync(CustomerProductSearchRequest request)
    {
        request ??= new CustomerProductSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize,
        });
        var where = new StringBuilder("WHERE ISNULL(cp.IsDeleted, 0) = 0 AND ISNULL(c.IsDeleted, 0) = 0");

        if (request.CustomerId != Guid.Empty)
        {
            where.AppendLine(" AND cp.CustomerId = @CustomerId");
            parameters.Add("CustomerId", request.CustomerId);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            where.AppendLine("""
                AND (
                       cp.CustomerSku LIKE @Keyword ESCAPE '~'
                    OR cp.Sku LIKE @Keyword ESCAPE '~'
                    OR cp.LinkSku LIKE @Keyword ESCAPE '~'
                    OR cp.CustomerPdDescription LIKE @Keyword ESCAPE '~'
                    OR cp.CustomerPdSeries LIKE @Keyword ESCAPE '~'
                )
                """);
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }

        var sql = $"""
            SELECT COUNT(1)
            {FromAndJoins}
            {where};

            SELECT {SelectColumns}
            {FromAndJoins}
            {where}
            ORDER BY cp.CustomerSku ASC, cp.Uid ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<CustomerProductDto>();
        return new PagedResponseBase<CustomerProductDto>
        {
            Items = items.ToList(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
    }

    public async Task<IReadOnlyList<Guid>> GetAllCustomerIdListAsync()
    {
        const string sql = """
            SELECT DISTINCT CustomerId
            FROM dbo.CustomerProduct
            WHERE CustomerId IS NOT NULL AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<Guid>(sql, transaction: Transaction)).ToList();
    }

    public async Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku, Guid customerId)
    {
        const string sql = """
            SELECT * FROM dbo.CustomerProduct WITH (NOLOCK)
            WHERE CustomerSku = @CustomerSku AND CustomerId = @CustomerId
              AND ISNULL(IsDeleted, 0) = 0;
            """;
        return await Connection.QueryFirstOrDefaultAsync<CustomerProduct>(
            sql, new { CustomerSku = customerSku, CustomerId = customerId }, transaction: Transaction);
    }

    public async Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(
        IEnumerable<string> skuList,
        Guid customerId)
    {
        const string sql = """
            SELECT CustomerSku FROM dbo.CustomerProduct
            WHERE CustomerSku IN @SkuList AND CustomerId = @CustomerId
              AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<string>(
            sql, new { SkuList = skuList, CustomerId = customerId }, transaction: Transaction)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetCustomerSkusUsedByOtherRecordsAsync(
        Guid customerId,
        IEnumerable<(Guid Uid, string CustomerSku)> items)
    {
        var materialized = items.ToArray();
        if (materialized.Length == 0) return [];
        const string sql = """
            SELECT DISTINCT CustomerSku FROM dbo.CustomerProduct
            WHERE CustomerId = @CustomerId
              AND CustomerSku IN @Codes
              AND Uid NOT IN @Uids
              AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<string>(sql, new
        {
            CustomerId = customerId,
            Codes = materialized.Select(x => x.CustomerSku),
            Uids = materialized.Select(x => x.Uid),
        }, transaction: Transaction)).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetCustomerIdsWithProductsAsync(IEnumerable<Guid> customerIds)
    {
        var ids = customerIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        const string sql = """
            SELECT DISTINCT CustomerId FROM dbo.CustomerProduct
            WHERE CustomerId IN @CustomerIds AND ISNULL(IsDeleted, 0) = 0;
            """;
        return (await Connection.QueryAsync<Guid>(
            sql, new { CustomerIds = ids }, transaction: Transaction)).ToList();
    }
}
