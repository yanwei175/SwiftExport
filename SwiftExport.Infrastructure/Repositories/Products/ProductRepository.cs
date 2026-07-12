using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData; // 引用 Core
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;
namespace SwiftExport.Infrastructure.Repositories.Products;



public partial class ProductRepository(RepositoryBaseContext context) : DapperRepository<Product>(context), IProductRepository
{
    private static readonly string CustomerProductTable = EntityMetadataCache.GetTableName<CustomerProduct>();
    private static readonly string CustomerTable = EntityMetadataCache.GetTableName<Customer>();
    private static readonly string ProductColorTable = EntityMetadataCache.GetTableName<ProductColor>();
    private static readonly string ProductCategoryTable = EntityMetadataCache.GetTableName<ProductCategory>();
    private static readonly string ProductPackageTable = EntityMetadataCache.GetTableName<ProductPackage>();

    public async Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync()
    {
        var sql = $"SELECT CustomerCode FROM [{TableName}] WHERE CustomerCode <> '' AND IsDeleted = 0 GROUP BY CustomerCode";
        var rst = await Connection.QueryAsync<string>(sql, transaction: Transaction);
        return [.. rst];
    }

    public async Task<Product?> GetBySkuAsync(string sku)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Sku] = @Sku AND IsDeleted = 0";
        return await Connection.QueryFirstOrDefaultAsync<Product>(sql, new { Sku = sku }, transaction: Transaction);
    }

    /// <summary>
    /// 按 SKU 列表批量查询产品。
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList)
    {
        if (skuList == null) return [];

        var normalized = skuList
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0) return [];

        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Sku] IN @SkuList AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        var rst = await Connection.QueryAsync<Product>(sql, new { SkuList = normalized }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList)
    {
        var sql = $"SELECT Sku FROM [{TableName}] WHERE  Sku IN @SkuList";
        var rst = await Connection.QueryAsync<string>(sql, new { SkuList = skuList }, transaction: Transaction);
        return [.. rst];
    }

    public async Task<PagedResponseBase<ProductDto>> GetPagedAsync(ProductSearchRequest request)
    {
        request ??= new ProductSearchRequest();

        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;

        if (pageSize > 500)
            pageSize = 500;

        var parameters = new DynamicParameters();
        parameters.Add("@Offset", (pageIndex - 1) * pageSize);
        parameters.Add("@Limit", pageSize);

        var whereSb = new StringBuilder();
        whereSb.AppendLine($@"WHERE (p.{nameof(Product.IsDeleted)} = 0 OR p.{nameof(Product.IsDeleted)} IS NULL)");

        // =========================================================
        // 1. 普通关键词
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();

            whereSb.AppendLine($@"
                                AND (
                                       p.{nameof(Product.Sku)}             LIKE @Keyword
                                    OR p.{nameof(Product.DescriptionEn)}     LIKE @Keyword
                                    OR p.{nameof(Product.DescriptionZh)}     LIKE @Keyword
                                    OR pc.{nameof(ProductColor.ColorCode)}       LIKE @Keyword
                                    OR pc.{nameof(ProductColor.ColorNameEn)}     LIKE @Keyword
                                    OR pc.{nameof(ProductColor.ColorNameZh)}     LIKE @Keyword
                                    OR pat.{nameof(ProductCategory.CategoryName)}     LIKE @Keyword
                                )");

            parameters.Add("@Keyword", $"%{keyword}%");
        }

        // =========================================================
        // 2. 批量关键词
        // =========================================================
        var batchKeywords = request.BatchKeywords?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(300)
            .ToList() ?? [];

        if (batchKeywords.Count > 0)
        {
            whereSb.AppendLine("AND (");

            for (var i = 0; i < batchKeywords.Count; i++)
            {
                var paramName = $"@BatchKeyword{i}";

                if (i > 0)
                    whereSb.AppendLine("OR");

                whereSb.AppendLine($@"
                                    (
                                           p.{nameof(Product.Sku)}              LIKE {paramName}
                                        OR p.{nameof(Product.DescriptionEn)}    LIKE {paramName}
                                        OR p.{nameof(Product.DescriptionZh)}    LIKE {paramName}
                                        OR pc.{nameof(ProductColor.ColorCode)}         LIKE {paramName}
                                        OR pc.{nameof(ProductColor.ColorNameEn)}       LIKE {paramName}
                                        OR pc.{nameof(ProductColor.ColorNameZh)}       LIKE {paramName}
                                    )");

                parameters.Add(paramName, $"%{batchKeywords[i]}%");
            }

            whereSb.AppendLine(")");
        }

        // =========================================================
        // 3. 颜色代码
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.ColorCodeUnique))
        {
            whereSb.AppendLine(@$"AND pc.{nameof(ProductColor.ColorCodeUnique)} = @ColorCodeUnique");
            parameters.Add("@ColorCodeUnique", request.ColorCodeUnique.Trim());
        }

        // =========================================================
        // 5. 物料类型
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.MaterialType))
        {
            whereSb.AppendLine($@"AND p.{nameof(Product.MaterialType)} = @MaterialType");
            parameters.Add("@MaterialType", request.MaterialType.Trim());
        }

        // =========================================================
        // 6. 分类名称
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.CategoryName))
        {
            whereSb.AppendLine($@"AND pat.{nameof(ProductCategory.CategoryName)} LIKE @CategoryName");
            parameters.Add("@CategoryName", $"%{request.CategoryName.Trim()}%");
        }

        // =========================================================
        // 7. 分类 ID 单选
        // =========================================================
        if (request.CategoryId != Guid.Empty)
        {
            whereSb.AppendLine($@"AND p.{nameof(Product.CategoryId)} = @CategoryId");
            parameters.Add("@CategoryId", request.CategoryId);
        }

        // =========================================================
        // 8. 分类 ID 多选
        // =========================================================
        var categoryIds = request.CategoryIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        if (categoryIds.Count > 0)
        {
            whereSb.AppendLine($@"AND p.{nameof(Product.CategoryId)} IN @CategoryIds");
            parameters.Add("@CategoryIds", categoryIds);
        }

        // =========================================================
        // 9. 客户 ID 单选
        // 用 EXISTS，避免 JOIN CustomerProduct 导致产品重复
        // =========================================================
        if (request.CustomerId != Guid.Empty)
        {
            whereSb.AppendLine($@"
                                AND EXISTS (
                                    SELECT 1
                                    FROM [{CustomerProductTable}] cp WITH(NOLOCK)
                                    WHERE cp.{nameof(CustomerProduct.SkuId)} = p.{nameof(Product.Uid)}
                                      AND cp.{nameof(CustomerProduct.CustomerId)} = @CustomerId
                                      AND (cp.{nameof(CustomerProduct.IsDeleted)} = 0 OR cp.{nameof(CustomerProduct.IsDeleted)} IS NULL)
                                )");

            parameters.Add("@CustomerId", request.CustomerId);
        }

        // =========================================================
        // 10. 客户 ID 多选
        // =========================================================
        var customerIds = request.CustomerIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (customerIds.Count > 0)
        {
            whereSb.AppendLine($@"
                                AND EXISTS (
                                    SELECT 1
                                    FROM [{CustomerProductTable}] cp WITH(NOLOCK)
                                    WHERE cp.{nameof(CustomerProduct.SkuId)} = p.{nameof(Product.Uid)}
                                      AND cp.{nameof(CustomerProduct.CustomerId)} IN @CustomerIds
                                      AND (cp.{nameof(CustomerProduct.IsDeleted)} = 0 OR cp.{nameof(CustomerProduct.IsDeleted)} IS NULL)
                                )");

            parameters.Add("@CustomerIds", customerIds);
        }

        // =========================================================
        // 11. 客户代码多选
        // =========================================================
        var customerCodes = request.CustomerCodes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (customerCodes.Count > 0)
        {
            whereSb.AppendLine($@"
                                AND EXISTS (
                                    SELECT 1
                                    FROM [{CustomerProductTable}] cp WITH(NOLOCK)
                                    INNER JOIN [{CustomerTable}] c WITH(NOLOCK)
                                        ON cp.{nameof(CustomerProduct.CustomerId)} = c.{nameof(Customer.Uid)}
                                    WHERE cp.{nameof(CustomerProduct.SkuId)} = p.{nameof(Product.Uid)}
                                      AND c.{nameof(Customer.CustomerCode)} IN @CustomerCodes
                                      AND (cp.{nameof(CustomerProduct.IsDeleted)} = 0 OR cp.{nameof(CustomerProduct.IsDeleted)} IS NULL)
                                      AND (c.{nameof(Customer.IsDeleted)} = 0 OR c.{nameof(Customer.IsDeleted)} IS NULL)
                                )");

            parameters.Add("@CustomerCodes", customerCodes);
        }

        var whereClause = whereSb.ToString();

        var countFromClause = $@"
                            FROM [{TableName}] p WITH(NOLOCK)

                            LEFT JOIN [{ProductColorTable}] pc WITH(NOLOCK)
                                ON p.{nameof(Product.ColorId)} = pc.{nameof(ProductColor.Uid)}
                               AND (pc.{nameof(ProductColor.IsDeleted)} = 0 OR pc.{nameof(ProductColor.IsDeleted)} IS NULL)

                            LEFT JOIN [{ProductCategoryTable}] pat WITH(NOLOCK)
                                ON p.{nameof(Product.CategoryId)} = pat.{nameof(ProductCategory.Uid)}
                               AND (pat.{nameof(ProductCategory.IsDeleted)} = 0 OR pat.{nameof(ProductCategory.IsDeleted)} IS NULL)
                            ";

        var dataFromClause = countFromClause + $@"
                                            OUTER APPLY (
                                                SELECT TOP 1
                                                    pp.{nameof(ProductPackage.PcsPerPackage)},
                                                    pp.{nameof(ProductPackage.Nw_KG)},
                                                    pp.{nameof(ProductPackage.Gw_KG)},
                                                    pp.{nameof(ProductPackage.Cbm)}
                                                FROM [{ProductPackageTable}] pp WITH(NOLOCK)
                                                WHERE pp.{nameof(ProductPackage.ProductId)} = p.{nameof(Product.Uid)}
                                                  AND (pp.{nameof(ProductPackage.IsDeleted)} = 0 OR pp.{nameof(ProductPackage.IsDeleted)} IS NULL)
                                                ORDER BY
                                                    pp.{nameof(ProductPackage.IsDefault)} DESC,
                                                    pp.{nameof(ProductPackage.PcsPerPackage)} DESC
                                            ) AS pkg
                                            ";

        var sql = $@"
                    SELECT COUNT(1)
                    {countFromClause}
                    {whereClause};

                    SELECT
                        p.*,

                        pat.{nameof(ProductCategory.CategoryName)},
                        pat.{nameof(ProductCategory.FakeName)},

                        pc.{nameof(ProductColor.ColorCodeUnique)},
                        pc.{nameof(ProductColor.ColorCode)},
                        pc.{nameof(ProductColor.ColorNameEn)},
                        pc.{nameof(ProductColor.ColorNameZh)} AS {nameof(ProductDto.Finish)},

                        pkg.{nameof(ProductPackage.PcsPerPackage)} AS {nameof(ProductDto.PcsOfCarton)},
                        pkg.{nameof(ProductPackage.Nw_KG)} AS {nameof(ProductDto.NwOfCarton)},
                        pkg.{nameof(ProductPackage.Gw_KG)} AS {nameof(ProductDto.GwOfCarton)},
                        pkg.{nameof(ProductPackage.Cbm)} AS {nameof(ProductDto.CbmOfCarton)}

                    {dataFromClause}
                    {whereClause}
                    ORDER BY p.{nameof(Product.Sku)} DESC
                    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
                    ";

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("执行产品分页查询 SQL: {Sql}", sql);
        }

        using var multi = await Connection.QueryMultipleAsync(
            sql,
            parameters,
            transaction: Transaction);

        var totalCount = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<ProductDto>();

        return new PagedResponseBase<ProductDto>
        {
            TotalCount = totalCount,
            PageIndex = pageIndex,
            PageSize = pageSize,
            Items = items.ToList()
        };
    }



}
