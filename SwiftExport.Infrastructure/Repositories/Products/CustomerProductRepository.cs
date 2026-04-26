using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories.Products;

public partial class CustomerProductRepository(RepositoryBaseContext context)
    : DapperRepository<CustomerProduct>(context), ICustomerProductRepository
{
    // ============================================================
    // 按 ID 联表查询单条，返回完整 DTO
    // ============================================================
    /// <summary>
    /// 按 ID 联表查询，返回含 CustomerCode / DescriptionEn / CategoryName 等联表字段的完整 DTO。
    /// </summary>
    public async Task<CustomerProductDto?> GetDtoByIdAsync(int id)
    {
        const string sql = @"
            SELECT
                cp.Id,
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
                cp.Price,
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
            LEFT JOIN [Product]         p  WITH(NOLOCK) ON cp.SkuId     = p.Id
            LEFT JOIN [Customer]        c  WITH(NOLOCK) ON cp.CustomerId = c.Id
            LEFT JOIN [ProductCategory] pc WITH(NOLOCK) ON p.CategoryId  = pc.Id
            WHERE cp.Id = @Id AND (cp.IsDeleted = 0 OR cp.IsDeleted IS NULL)";

        return await Connection.QueryFirstOrDefaultAsync<CustomerProductDto>(
            sql, new { Id = id }, transaction: Transaction);
    }

    // ============================================================
    // 联表分页查询（隐藏基类泛型 GetPagedListAsync）
    // ============================================================
    /// <summary>
    /// 联表分页查询，返回 CustomerProductPagedItem 读模型。
    /// 关联：CustomerProduct cp
    ///        LEFT JOIN Product  p  ON cp.SkuId = p.Id
    ///        LEFT JOIN Customer c  ON cp.CustomerId = c.Id
    ///        LEFT JOIN ProductCategory pc ON p.CategoryId = pc.Id
    /// criteria 字段说明：
    ///   - 直接写字段名（如 CustomerId / CustomerSku / Sku）→ 映射到 cp.字段
    ///   - CategoryId → 映射到 p.CategoryId
    /// </summary>
    public new async Task<PagedResult<CustomerProductDto>> GetPagedListAsync(
        QueryCriteria criteria,
        int pageIndex,
        int pageSize,
        string orderBy = "cp.Id DESC")
    {
        // 🛡️ orderBy 安全清洗
        if (string.IsNullOrWhiteSpace(orderBy))
            orderBy = "cp.Id DESC";
        else
            orderBy = OrderByRegex().Replace(orderBy, "");

        // ----------------------------------------------------------------
        // 构建 WHERE 子句（基类 ParseQueryCriteria 生成的条件作用于 cp 表，
        // 特殊字段 CategoryId 需额外处理加别名）
        // ----------------------------------------------------------------
        var parameters = new DynamicParameters();

        // 先用基类工具生成动态条件字符串（字段不带表别名）
        var rawWhere = ParseQueryCriteria(criteria, parameters);

        // 将 CategoryId 条件从 cp 改指向 p（ProductCategory 来自 Product.CategoryId）
        // ParseQueryCriteria 生成的格式为 [CategoryId]，替换为 p.[CategoryId]
        if (!string.IsNullOrWhiteSpace(rawWhere))
            rawWhere = rawWhere.Replace("[CategoryId]", "p.[CategoryId]");

        // 将其余普通字段加上 cp. 表别名（避免多表同名字段歧义）
        // 使用简单规则：将 [字段名] 替换为 cp.[字段名]，已处理过的 p.[...] 跳过
        if (!string.IsNullOrWhiteSpace(rawWhere))
            rawWhere = System.Text.RegularExpressions.Regex.Replace(
                rawWhere,
                @"(?<!\w\.)\[(\w+)\]",   // 匹配没有表前缀的 [字段名]
                "cp.[$1]");

        // 固定基础条件：只查未删除的客户产品记录
        var baseSb = new StringBuilder("WHERE (cp.IsDeleted = 0 OR cp.IsDeleted IS NULL)");
        if (!string.IsNullOrWhiteSpace(rawWhere))
            baseSb.Append(rawWhere); // rawWhere 自带 AND 开头

        string whereClause = baseSb.ToString();

        // ----------------------------------------------------------------
        // 联表查询主体
        // ----------------------------------------------------------------
        const string joinClause = @"
            FROM [CustomerProduct] cp WITH(NOLOCK)
            LEFT JOIN [Product]         p   WITH(NOLOCK) ON cp.SkuId      = p.Id
            LEFT JOIN [Customer]        c   WITH(NOLOCK) ON cp.CustomerId  = c.Id
            LEFT JOIN [ProductCategory] pc  WITH(NOLOCK) ON p.CategoryId   = pc.Id";

        // 分页参数
        if (pageIndex < 1) pageIndex = 1;
        int offset = (pageIndex - 1) * pageSize;
        parameters.Add("@Offset", offset);
        parameters.Add("@Limit", pageSize);

        var sql = $@"
            SELECT COUNT(1) {joinClause} {whereClause};

            SELECT
                cp.Id,
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
                cp.Price,
                cp.Currency,
                cp.LeadTimeDays,
                cp.MOQ,
                cp.IsActive,
                -- JOIN 补充字段
                c.CustomerCode,
                p.DescriptionEn,
                p.MainImage,
                pc.CategoryName
            {joinClause}
            {whereClause}
            ORDER BY {orderBy}
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        // ★ Debug 日志追踪
        if (Logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug))
        {
            Logger.LogDebug("执行客户产品联表分页查询，生成的 SQL: {Sql}", sql);
        }

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<CustomerProductDto>();

        return new PagedResult<CustomerProductDto>
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
    public async Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku, int customerId)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [CustomerSku] = @Sku AND [CustomerId] = @CustomerId AND IsDeleted = 0";
        return await Connection.QueryFirstOrDefaultAsync<CustomerProduct>(
            sql, new { Sku = customerSku, CustomerId = customerId }, transaction: Transaction);
    }

    /// <summary>
    /// 批量检查客户料号是否已存在（用于新增前去重校验）
    /// </summary>
    public async Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(IEnumerable<string> skuList, int customerId)
    {
        var sql = $"SELECT CustomerSku FROM [{TableName}] WHERE CustomerSku IN @SkuList AND CustomerId = @CustomerId AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<string>(sql, new { SkuList = skuList, CustomerId = customerId }, transaction: Transaction);
        return [.. rst];
    }

    // 正则安全过滤（orderBy 字段名白名单）
    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
    private static partial System.Text.RegularExpressions.Regex OrderByRegex();
}

