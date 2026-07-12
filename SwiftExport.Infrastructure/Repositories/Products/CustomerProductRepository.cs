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

public partial class CustomerProductRepository(RepositoryBaseContext context)
    : DapperRepository<CustomerProduct>(context), ICustomerProductRepository
{
    private static readonly string CustomerPoConfigTable = EntityMetadataCache.GetTableName<CustomerPoConfig>();

    // ============================================================
    // 按 ID 联表查询单条，返回完整 DTO
    // ============================================================
    /// <summary>
    /// 按 ID 联表查询，返回含 CustomerCode / DescriptionEn / CategoryName 等联表字段的完整 DTO。
    /// </summary>
    public async Task<CustomerProductDto?> GetDtoByIdAsync(Guid uid)
    {
        const string sql = @"
            SELECT
                cp.Uid,
                cp.CustomerId,
                cp.SkuId,
                cp.Sku,
                cp.LinkSku,
                cp.CustomerSku,
                cp.CustomerPdSeries,
                cp.CustomerPdDescription,
                cp.CustomerColorName,
                cp.CustomerPdFeatures,
                cp.CustomerBarcode1,
                cp.CustomerBarcode2,
                cp.CustomerSkuUnit,
                cp.USDPrice,
                cp.CNYPrice,
                cp.Currency,
                cp.LeadTimeDays,
                cp.MOQ,
                cp.IsActive,
                cp.IsDeleted,
                c.CustomerCode,
                p.DescriptionEn,
                p.MainImage,
                pc.CategoryName
            FROM [CustomerProduct] cp WITH(NOLOCK)
            LEFT JOIN [Product]         p  WITH(NOLOCK) ON cp.SkuId     = p.Uid
            LEFT JOIN [Customer]        c  WITH(NOLOCK) ON cp.CustomerId = c.Uid
            LEFT JOIN [ProductCategory] pc WITH(NOLOCK) ON p.CategoryId  = pc.Uid
            WHERE cp.Uid = @Uid AND (cp.IsDeleted = 0 OR cp.IsDeleted IS NULL)";

        return await Connection.QueryFirstOrDefaultAsync<CustomerProductDto>(
            sql, new { Uid = uid }, transaction: Transaction);
    }

    /// <summary>
    /// 联表分页查询，返回 CustomerProductPagedItem 读模型。
    /// 关联：CustomerProduct cp
    ///        LEFT JOIN Product  p  ON cp.SkuId = p.Uid
    ///        LEFT JOIN Customer c  ON cp.CustomerId = c.Uid
    ///        LEFT JOIN ProductCategory pc ON p.CategoryId = pc.Uid
    /// criteria 字段说明：
    ///   - 直接写字段名（如 CustomerId / CustomerSku / Sku）→ 映射到 cp.字段
    ///   - CategoryId → 映射到 p.CategoryId
    /// </summary>
    public async Task<PagedResponseBase<CustomerProductDto>> GetPagedAsync(CustomerProductSearchRequest request)
    {
        request ??= new CustomerProductSearchRequest();

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        // 防止前端一次请求太多
        if (pageSize > 500)
            pageSize = 500;

        var offset = (pageIndex - 1) * pageSize;

        var parameters = new DynamicParameters();

        var whereSb = new StringBuilder();
        whereSb.AppendLine(@$"WHERE ISNULL(cpc.{nameof(CustomerProduct.IsDeleted)}, 0) = 0");

        // =========================================================
        // 1. 普通模糊关键词
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            whereSb.AppendLine(@$"
                               AND (
                                      {nameof(CustomerProduct.CustomerCode)}       LIKE @Keyword
                                   OR {nameof(CustomerProduct.Sku)}       LIKE @Keyword
                               )");

            parameters.Add("@Keyword", $"%{keyword}%");
        }

        var whereClause = whereSb.ToString();

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var sql = $@"
                        SELECT COUNT(1)
                        FROM
                        {TableName}
                        {whereClause};
                        SELECT *
                        FROM
                        {CustomerPoConfigTable}
                        {whereClause}
                        ORDER BY {nameof(CustomerProduct.CreatedDate)} DESC
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                        ";


        using var multi = await Connection.QueryMultipleAsync(
            sql,
            parameters,
            transaction: Transaction);

        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<CustomerProductDto>();

        return new PagedResponseBase<CustomerProductDto>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    // ============================================================
    // 其他查询方法
    // ============================================================

    /// <summary>
    /// 获取所有有客户产品记录的 CustomerId 列表（去重）
    /// </summary>
    public async Task<IReadOnlyList<int>> GetAllCustomerIdListAsync()
    {
        var sql = $"SELECT CustomerId FROM [{TableName}] WHERE CustomerId IS NOT NULL AND IsDeleted = 0 GROUP BY CustomerId";
        var rst = await Connection.QueryAsync<int>(sql, transaction: Transaction);
        return [.. rst];
    }

    /// <summary>
    /// 按客户料号 + 客户 ID 精确查找一条客户产品记录
    /// </summary>
    public async Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku, Guid customerId)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [CustomerSku] = @Sku AND [CustomerId] = @CustomerId AND IsDeleted = 0";
        return await Connection.QueryFirstOrDefaultAsync<CustomerProduct>(
            sql, new { Sku = customerSku, CustomerId = customerId }, transaction: Transaction);
    }

    /// <summary>
    /// 批量检查客户料号是否已存在（用于新增前去重校验）
    /// </summary>
    public async Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(IEnumerable<string> skuList, Guid customerId)
    {
        var sql = $"SELECT CustomerSku FROM [{TableName}] WHERE CustomerSku IN @SkuList AND CustomerId = @CustomerId AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<string>(sql, new { SkuList = skuList, CustomerId = customerId }, transaction: Transaction);
        return [.. rst];
    }

    public Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items)
    {
        throw new NotImplementedException();
    }
}

