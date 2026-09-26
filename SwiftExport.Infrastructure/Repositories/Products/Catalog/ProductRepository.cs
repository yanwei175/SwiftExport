using SwiftExport.Core.Interfaces.Products.Catalog;
using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData; // 引用 Core
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;
namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;



/// <summary>产品资料及专用查询仓储；普通资料保存不拥有业务启停资格。</summary>
public partial class ProductRepository(RepositoryBaseContext context) : DapperRepository<Product>(context), IProductRepository
{
    private static readonly string CustomerProductTable = EntityMetadataCache.GetTableName<CustomerProduct>();
    private static readonly string CustomerTable = EntityMetadataCache.GetTableName<Customer>();
    private static readonly string ProductColorTable = EntityMetadataCache.GetTableName<ProductColor>();
    private static readonly string ProductCategoryTable = EntityMetadataCache.GetTableName<ProductCategory>();
    private static readonly string ProductMasterTable = EntityMetadataCache.GetTableName<ProductMaster>();
    private static readonly string ProductPackageTable = EntityMetadataCache.GetTableName<ProductPackage>();
    private static readonly string SupplierTable = EntityMetadataCache.GetTableName<Supplier>();
    private static readonly string WarehouseSiteTable = EntityMetadataCache.GetTableName<WarehouseSite>();

    /// <summary>普通资料更新跳过生命周期状态与旧分类占位；默认供应商身份仍可由资料维护更新。</summary>
    protected override IReadOnlyList<string> UpdateProperties => [.. base.UpdateProperties
        .Where(property => !property.Equals(nameof(Product.IsActive), StringComparison.OrdinalIgnoreCase)
                           && !property.Equals("CategoryId", StringComparison.OrdinalIgnoreCase))];

    /// <summary>在调用方事务内以版本条件更新默认供应商身份，不缓存供应商当前编码。</summary>
    public async Task<RepositoryWriteResult> SetDefaultSupplierAsync(
        Guid productId,
        int expectedVersion,
        Guid supplierId)
    {
        const string sql = @"
UPDATE [dbo].[Product]
SET [DefaultSupplierId]=@SupplierId,
    [UpdatedDate]=@Now,
    [UpdatedBy]=@UserName,
    [Version]=[Version]+1
OUTPUT INSERTED.[Version]
WHERE [Uid]=@ProductId AND [Version]=@ExpectedVersion AND [IsDeleted]=0;";
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(sql, new
        {
            ProductId = productId,
            ExpectedVersion = expectedVersion,
            SupplierId = supplierId,
            Now = DateTime.Now,
            UserName = CurrentUser.UserName
        }, transaction: Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(productId, expectedVersion, newVersion.Value, "默认供应商设置成功。")
            : RepositoryWriteResult.Fail(productId, expectedVersion, "CONCURRENCY_CONFLICT", "产品已被其他用户修改，请刷新后重试。");
    }

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

    /// <summary>读取产品详情及当前参考资料；失效关联只隐藏名称，不删除产品或改写关联标识。</summary>
    public Task<ProductDto?> GetDtoByIdAsync(Guid uid)
    {
        var sql = $@"
SELECT p.{nameof(Product.Uid)},p.{nameof(Product.Version)},p.{nameof(Product.IsDeleted)},
       p.{nameof(Product.CreatedDate)},p.{nameof(Product.CreatedBy)},p.{nameof(Product.UpdatedDate)},p.{nameof(Product.UpdatedBy)},
       p.{nameof(Product.IsActive)},p.{nameof(Product.MasterSku)},p.{nameof(Product.Sku)},
       p.{nameof(Product.DescriptionEn)},p.{nameof(Product.DescriptionZh)},
       p.{nameof(Product.FeaturesEn)},p.{nameof(Product.FeaturesZh)},p.{nameof(Product.MaterialType)},
       p.{nameof(Product.Unit)},p.{nameof(Product.SupplyType)},p.{nameof(Product.BomVersion)},p.{nameof(Product.BomNo)},
       p.{nameof(Product.Note)},p.{nameof(Product.LinkSku)},p.{nameof(Product.MainImage)},
       p.{nameof(Product.CNYPrice)},p.{nameof(Product.USDPrice)},p.{nameof(Product.ColorId)},
       p.{nameof(Product.DefaultSupplierId)},p.{nameof(Product.DefaultWarehouseSiteId)},
       p.{nameof(Product.ManufacturerId)},p.{nameof(Product.ManufacturerModel)},
       manufacturer.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.ManufacturerCode)},
       manufacturer.{nameof(Supplier.SupplierName)} AS {nameof(ProductDto.ManufacturerName)},
       warehouse.{nameof(WarehouseSite.Code)} AS {nameof(ProductDto.DefaultWarehouseCode)},
       warehouse.{nameof(WarehouseSite.Name)} AS {nameof(ProductDto.DefaultWarehouseName)},
       defaultSupplier.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.DefaultSupplierCode)},
       defaultSupplier.{nameof(Supplier.SupplierName)} AS {nameof(ProductDto.SupplierName)}
FROM [{TableName}] p
LEFT JOIN [{SupplierTable}] manufacturer
    ON manufacturer.{nameof(Supplier.Uid)}=p.{nameof(Product.ManufacturerId)}
   AND manufacturer.{nameof(Supplier.IsDeleted)}=0
LEFT JOIN [{WarehouseSiteTable}] warehouse
    ON warehouse.{nameof(WarehouseSite.Uid)}=p.{nameof(Product.DefaultWarehouseSiteId)}
   AND warehouse.{nameof(WarehouseSite.IsDeleted)}=0
LEFT JOIN [{SupplierTable}] defaultSupplier
    ON defaultSupplier.{nameof(Supplier.Uid)}=p.{nameof(Product.DefaultSupplierId)}
   AND defaultSupplier.{nameof(Supplier.IsDeleted)}=0
WHERE p.{nameof(Product.Uid)}=@Uid
  AND (p.{nameof(Product.IsDeleted)}=0 OR p.{nameof(Product.IsDeleted)} IS NULL);";
        return Connection.QuerySingleOrDefaultAsync<ProductDto>(sql, new { Uid = uid }, transaction: Transaction);
    }

    /// <summary>按 SKU 返回产品资料，并从所属主型号实时投影分类。</summary>
    public async Task<ProductDto?> GetDtoBySkuAsync(string sku)
    {
        var sql = $@"
SELECT TOP (1)
    p.{nameof(Product.Uid)}, p.{nameof(Product.Version)}, p.{nameof(Product.IsDeleted)},
    p.{nameof(Product.CreatedDate)}, p.{nameof(Product.CreatedBy)}, p.{nameof(Product.UpdatedDate)}, p.{nameof(Product.UpdatedBy)},
    p.{nameof(Product.MasterSku)}, p.{nameof(Product.Sku)}, p.{nameof(Product.DescriptionEn)}, p.{nameof(Product.DescriptionZh)},
    p.{nameof(Product.FeaturesEn)}, p.{nameof(Product.FeaturesZh)}, p.{nameof(Product.MaterialType)}, p.{nameof(Product.Unit)},
    p.{nameof(Product.SupplyType)}, p.{nameof(Product.BomVersion)}, p.{nameof(Product.BomNo)}, p.{nameof(Product.Note)},
    p.{nameof(Product.LinkSku)}, p.{nameof(Product.CNYPrice)}, p.{nameof(Product.USDPrice)}, p.{nameof(Product.ColorId)},
    p.{nameof(Product.DefaultSupplierId)}, p.{nameof(Product.MainImage)},
    p.{nameof(Product.DefaultWarehouseSiteId)}, p.{nameof(Product.ManufacturerId)}, p.{nameof(Product.ManufacturerModel)},
    COALESCE(pm.{nameof(ProductMaster.CategoryId)}, @EmptyGuid) AS {nameof(ProductDto.CategoryId)},
    CASE WHEN pm.{nameof(ProductMaster.Uid)} IS NULL
           OR pm.{nameof(ProductMaster.CategoryId)} IS NULL
           OR pm.{nameof(ProductMaster.CategoryId)}=@EmptyGuid THEN N'未分类'
         ELSE COALESCE(pat.{nameof(ProductCategory.CategoryName)}, N'分类已失效') END AS {nameof(ProductDto.CategoryName)},
    defaultSupplier.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.DefaultSupplierCode)},
    defaultSupplier.{nameof(Supplier.SupplierName)} AS {nameof(ProductDto.SupplierName)},
    manufacturer.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.ManufacturerCode)},
    manufacturer.{nameof(Supplier.SupplierName)} AS {nameof(ProductDto.ManufacturerName)},
    warehouse.{nameof(WarehouseSite.Code)} AS {nameof(ProductDto.DefaultWarehouseCode)},
    warehouse.{nameof(WarehouseSite.Name)} AS {nameof(ProductDto.DefaultWarehouseName)},
    pc.{nameof(ProductColor.ColorNameZh)} AS {nameof(ProductDto.Finish)}
FROM [{TableName}] p WITH(NOLOCK)
LEFT JOIN [{ProductMasterTable}] pm WITH(NOLOCK)
    ON pm.{nameof(ProductMaster.Uid)}=p.{nameof(Product.ProductMasterId)}
   AND (pm.{nameof(ProductMaster.IsDeleted)}=0 OR pm.{nameof(ProductMaster.IsDeleted)} IS NULL)
LEFT JOIN [{ProductCategoryTable}] pat WITH(NOLOCK)
    ON pat.{nameof(ProductCategory.Uid)}=pm.{nameof(ProductMaster.CategoryId)}
   AND (pat.{nameof(ProductCategory.IsDeleted)}=0 OR pat.{nameof(ProductCategory.IsDeleted)} IS NULL)
LEFT JOIN [{SupplierTable}] defaultSupplier WITH(NOLOCK)
    ON defaultSupplier.{nameof(Supplier.Uid)}=p.{nameof(Product.DefaultSupplierId)}
   AND (defaultSupplier.{nameof(Supplier.IsDeleted)}=0 OR defaultSupplier.{nameof(Supplier.IsDeleted)} IS NULL)
LEFT JOIN [{SupplierTable}] manufacturer WITH(NOLOCK)
    ON manufacturer.{nameof(Supplier.Uid)}=p.{nameof(Product.ManufacturerId)}
   AND manufacturer.{nameof(Supplier.IsDeleted)}=0
LEFT JOIN [{WarehouseSiteTable}] warehouse WITH(NOLOCK)
    ON warehouse.{nameof(WarehouseSite.Uid)}=p.{nameof(Product.DefaultWarehouseSiteId)}
   AND warehouse.{nameof(WarehouseSite.IsDeleted)}=0
LEFT JOIN [{ProductColorTable}] pc WITH(NOLOCK)
    ON pc.{nameof(ProductColor.Uid)}=p.{nameof(Product.ColorId)}
   AND (pc.{nameof(ProductColor.IsDeleted)}=0 OR pc.{nameof(ProductColor.IsDeleted)} IS NULL)
WHERE p.{nameof(Product.Sku)}=@Sku
  AND (p.{nameof(Product.IsDeleted)}=0 OR p.{nameof(Product.IsDeleted)} IS NULL)
ORDER BY p.{nameof(Product.Uid)};";
        return await Connection.QueryFirstOrDefaultAsync<ProductDto>(
            sql,
            new { Sku = sku, EmptyGuid = Guid.Empty },
            transaction: Transaction);
    }

    public Task<Product?> GetByBomNoAsync(string bomNo)
    {
        if (string.IsNullOrWhiteSpace(bomNo)) return Task.FromResult<Product?>(null);
        var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK) WHERE [BomNo]=@BomNo AND [IsDeleted]=0 ORDER BY [Uid]";
        return Connection.QueryFirstOrDefaultAsync<Product>(sql, new { BomNo = bomNo.Trim() }, transaction: Transaction);
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

    /// <summary>
    /// 为产品 Lookup 返回轻量候选。CustomerProduct 使用 EXISTS，避免同一产品重复返回。
    /// </summary>
    public async Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var keyword = request.Keyword?.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 50);
        var parameters = new
        {
            Limit = limit,
            request.ExcludeProductId,
            ExactKeyword = keyword,
            PrefixKeyword = $"{keyword}%",
            ContainsKeyword = $"%{keyword}%",
            EmptyGuid = Guid.Empty
        };

        var sql = $@"
            SELECT TOP (@Limit)
                p.{nameof(Product.Uid)} AS {nameof(ProductLookupDto.Uid)},
                p.{nameof(Product.Sku)} AS {nameof(ProductLookupDto.Sku)},
                p.{nameof(Product.DescriptionEn)} AS {nameof(ProductLookupDto.DescriptionEn)},
                p.{nameof(Product.DescriptionZh)} AS {nameof(ProductLookupDto.DescriptionZh)},
                p.{nameof(Product.LinkSku)} AS {nameof(ProductLookupDto.LinkSku)},
                CASE
                    WHEN pm.{nameof(ProductMaster.Uid)} IS NULL
                      OR pm.{nameof(ProductMaster.CategoryId)} IS NULL
                      OR pm.{nameof(ProductMaster.CategoryId)} = @EmptyGuid THEN N'未分类'
                    ELSE COALESCE(pat.{nameof(ProductCategory.CategoryName)}, N'分类已失效')
                END AS {nameof(ProductLookupDto.CategoryName)},
                p.{nameof(Product.MainImage)} AS {nameof(ProductLookupDto.MainImage)},
                pc.{nameof(ProductColor.ColorNameZh)} AS {nameof(ProductLookupDto.Finish)},
                p.{nameof(Product.FeaturesEn)} AS {nameof(ProductLookupDto.FeaturesEn)},
                p.{nameof(Product.FeaturesZh)} AS {nameof(ProductLookupDto.FeaturesZh)},
                p.{nameof(Product.MaterialType)} AS {nameof(ProductLookupDto.MaterialType)},
                p.{nameof(Product.USDPrice)} AS {nameof(ProductLookupDto.USDPrice)},
                p.{nameof(Product.CNYPrice)} AS {nameof(ProductLookupDto.CNYPrice)}
            FROM [{TableName}] p WITH(NOLOCK)
            LEFT JOIN [{ProductMasterTable}] pm WITH(NOLOCK)
                ON pm.{nameof(ProductMaster.Uid)} = p.{nameof(Product.ProductMasterId)}
               AND (pm.{nameof(ProductMaster.IsDeleted)} = 0 OR pm.{nameof(ProductMaster.IsDeleted)} IS NULL)
            LEFT JOIN [{ProductCategoryTable}] pat WITH(NOLOCK)
                ON pat.{nameof(ProductCategory.Uid)} = pm.{nameof(ProductMaster.CategoryId)}
               AND (pat.{nameof(ProductCategory.IsDeleted)} = 0 OR pat.{nameof(ProductCategory.IsDeleted)} IS NULL)
            LEFT JOIN [{ProductColorTable}] pc WITH(NOLOCK)
                ON pc.{nameof(ProductColor.Uid)} = p.{nameof(Product.ColorId)}
               AND (pc.{nameof(ProductColor.IsDeleted)} = 0 OR pc.{nameof(ProductColor.IsDeleted)} IS NULL)
            WHERE (p.{nameof(Product.IsDeleted)} = 0 OR p.{nameof(Product.IsDeleted)} IS NULL)
              AND (@ExcludeProductId = '00000000-0000-0000-0000-000000000000'
                   OR p.{nameof(Product.Uid)} <> @ExcludeProductId)
              AND (
                     p.{nameof(Product.Sku)} LIKE @ContainsKeyword
                  OR p.{nameof(Product.DescriptionEn)} LIKE @ContainsKeyword
                  OR p.{nameof(Product.DescriptionZh)} LIKE @ContainsKeyword
                  OR p.{nameof(Product.LinkSku)} LIKE @ContainsKeyword
                  OR pat.{nameof(ProductCategory.CategoryName)} LIKE @ContainsKeyword
                  OR EXISTS (
                      SELECT 1
                      FROM [{CustomerProductTable}] cp WITH(NOLOCK)
                      WHERE cp.{nameof(CustomerProduct.SkuId)} = p.{nameof(Product.Uid)}
                        AND (cp.{nameof(CustomerProduct.IsDeleted)} = 0 OR cp.{nameof(CustomerProduct.IsDeleted)} IS NULL)
                        AND cp.{nameof(CustomerProduct.CustomerSku)} LIKE @ContainsKeyword
                  )
              )
            ORDER BY
                CASE
                    WHEN p.{nameof(Product.Sku)} = @ExactKeyword THEN 0
                    WHEN EXISTS (
                             SELECT 1
                             FROM [{CustomerProductTable}] exactCp WITH(NOLOCK)
                             WHERE exactCp.{nameof(CustomerProduct.SkuId)} = p.{nameof(Product.Uid)}
                               AND (exactCp.{nameof(CustomerProduct.IsDeleted)} = 0 OR exactCp.{nameof(CustomerProduct.IsDeleted)} IS NULL)
                               AND exactCp.{nameof(CustomerProduct.CustomerSku)} = @ExactKeyword
                         ) THEN 1
                    WHEN p.{nameof(Product.Sku)} LIKE @PrefixKeyword THEN 2
                    WHEN EXISTS (
                             SELECT 1
                             FROM [{CustomerProductTable}] prefixCp WITH(NOLOCK)
                             WHERE prefixCp.{nameof(CustomerProduct.SkuId)} = p.{nameof(Product.Uid)}
                               AND (prefixCp.{nameof(CustomerProduct.IsDeleted)} = 0 OR prefixCp.{nameof(CustomerProduct.IsDeleted)} IS NULL)
                               AND prefixCp.{nameof(CustomerProduct.CustomerSku)} LIKE @PrefixKeyword
                         ) THEN 3
                    ELSE 4
                END,
                p.{nameof(Product.Sku)} ASC;";

        var items = await Connection.QueryAsync<ProductLookupDto>(
            sql,
            parameters,
            transaction: Transaction);
        return items.ToArray();
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
        parameters.Add("@EmptyGuid", Guid.Empty);

        var whereSb = new StringBuilder();
        whereSb.AppendLine($@"WHERE (p.{nameof(Product.IsDeleted)} = 0 OR p.{nameof(Product.IsDeleted)} IS NULL)");

        // =========================================================
        // 1. 普通关键词
        // =========================================================
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            whereSb.AppendLine($@"
                                AND (
                                       p.{nameof(Product.Sku)}             LIKE @Keyword ESCAPE '~'
                                    OR p.{nameof(Product.DescriptionEn)}     LIKE @Keyword ESCAPE '~'
                                    OR p.{nameof(Product.DescriptionZh)}     LIKE @Keyword ESCAPE '~'
                                    OR pc.{nameof(ProductColor.ColorCode)}       LIKE @Keyword ESCAPE '~'
                                    OR pc.{nameof(ProductColor.ColorNameEn)}     LIKE @Keyword ESCAPE '~'
                                    OR pc.{nameof(ProductColor.ColorNameZh)}     LIKE @Keyword ESCAPE '~'
                                    OR pat.{nameof(ProductCategory.CategoryName)}     LIKE @Keyword ESCAPE '~'
                                )");

            parameters.Add("@Keyword", SqlLikePattern.Build(request.Keyword));
        }

        // =========================================================
        // 2. 批量关键词（SKU、颜色代码精确匹配）
        // =========================================================
        var batchKeywords = request.BatchKeywords?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(300)
            .ToList() ?? [];

        if (batchKeywords.Count > 0)
        {
            whereSb.AppendLine($@"
                                AND (
                                       p.{nameof(Product.Sku)} IN @BatchKeywords
                                    OR pc.{nameof(ProductColor.ColorCode)} IN @BatchKeywords
                                )");
            parameters.Add("@BatchKeywords", batchKeywords);
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
        var materialTypeList = request.MaterialTypeList?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (materialTypeList.Count > 0)
        {
            whereSb.AppendLine($@"AND p.{nameof(Product.MaterialType)} IN @MaterialTypeList");
            parameters.Add("@MaterialTypeList", materialTypeList);
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
            whereSb.AppendLine($@"AND pm.{nameof(ProductMaster.CategoryId)} = @CategoryId");
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
            whereSb.AppendLine($@"AND pm.{nameof(ProductMaster.CategoryId)} IN @CategoryIds");
            parameters.Add("@CategoryIds", categoryIds);
        }

        // =========================================================
        // 8. 颜色 ID 多选
        // =========================================================
        var colorIds = request.ColorIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList() ?? [];

        if (colorIds.Count > 0)
        {
            whereSb.AppendLine($@"AND pc.{nameof(ProductColor.Uid)} IN @ColorIds");
            parameters.Add("@ColorIds", colorIds);
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

                            LEFT JOIN [{ProductMasterTable}] pm WITH(NOLOCK)
                                ON p.{nameof(Product.ProductMasterId)} = pm.{nameof(ProductMaster.Uid)}
                               AND (pm.{nameof(ProductMaster.IsDeleted)} = 0 OR pm.{nameof(ProductMaster.IsDeleted)} IS NULL)

                            LEFT JOIN [{ProductCategoryTable}] pat WITH(NOLOCK)
                                ON pm.{nameof(ProductMaster.CategoryId)} = pat.{nameof(ProductCategory.Uid)}
                               AND (pat.{nameof(ProductCategory.IsDeleted)} = 0 OR pat.{nameof(ProductCategory.IsDeleted)} IS NULL)

                            LEFT JOIN [{SupplierTable}] defaultSupplier WITH(NOLOCK)
                                ON p.{nameof(Product.DefaultSupplierId)} = defaultSupplier.{nameof(Supplier.Uid)}
                               AND (defaultSupplier.{nameof(Supplier.IsDeleted)} = 0 OR defaultSupplier.{nameof(Supplier.IsDeleted)} IS NULL)

                            LEFT JOIN [{SupplierTable}] manufacturer WITH(NOLOCK)
                                ON p.{nameof(Product.ManufacturerId)} = manufacturer.{nameof(Supplier.Uid)}
                               AND manufacturer.{nameof(Supplier.IsDeleted)} = 0

                            LEFT JOIN [{WarehouseSiteTable}] warehouse WITH(NOLOCK)
                                ON p.{nameof(Product.DefaultWarehouseSiteId)} = warehouse.{nameof(WarehouseSite.Uid)}
                               AND warehouse.{nameof(WarehouseSite.IsDeleted)} = 0
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
                        p.{nameof(Product.Uid)},
                        p.{nameof(Product.Version)},
                        p.{nameof(Product.IsDeleted)},
                        p.{nameof(Product.CreatedDate)},
                        p.{nameof(Product.CreatedBy)},
                        p.{nameof(Product.UpdatedDate)},
                        p.{nameof(Product.UpdatedBy)},
                        p.{nameof(Product.MasterSku)},
                        p.{nameof(Product.Sku)},
                        p.{nameof(Product.DescriptionEn)},
                        p.{nameof(Product.DescriptionZh)},
                        p.{nameof(Product.FeaturesEn)},
                        p.{nameof(Product.FeaturesZh)},
                        p.{nameof(Product.MaterialType)},
                        p.{nameof(Product.Unit)},
                        p.{nameof(Product.SupplyType)},
                        p.{nameof(Product.BomVersion)},
                        p.{nameof(Product.BomNo)},
                        p.{nameof(Product.Note)},
                        p.{nameof(Product.LinkSku)},
                        p.{nameof(Product.CNYPrice)},
                        p.{nameof(Product.USDPrice)},
                        p.{nameof(Product.ColorId)},
                        p.{nameof(Product.DefaultSupplierId)},
                        p.{nameof(Product.DefaultWarehouseSiteId)},
                        p.{nameof(Product.ManufacturerId)},
                        p.{nameof(Product.ManufacturerModel)},
                        p.{nameof(Product.MainImage)},
                        COALESCE(pm.{nameof(ProductMaster.CategoryId)}, @EmptyGuid) AS {nameof(ProductDto.CategoryId)},
                        CASE
                            WHEN pm.{nameof(ProductMaster.Uid)} IS NULL
                              OR pm.{nameof(ProductMaster.CategoryId)} IS NULL
                              OR pm.{nameof(ProductMaster.CategoryId)} = @EmptyGuid THEN N'未分类'
                            ELSE COALESCE(pat.{nameof(ProductCategory.CategoryName)}, N'分类已失效')
                        END AS {nameof(ProductDto.CategoryName)},
                        pat.{nameof(ProductCategory.FakeName)},

                        defaultSupplier.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.DefaultSupplierCode)},
                        defaultSupplier.{nameof(Supplier.SupplierName)} AS {nameof(ProductDto.SupplierName)},
                        manufacturer.{nameof(Supplier.SupplierCode)} AS {nameof(ProductDto.ManufacturerCode)},
                        manufacturer.{nameof(Supplier.SupplierName)} AS {nameof(ProductDto.ManufacturerName)},
                        warehouse.{nameof(WarehouseSite.Code)} AS {nameof(ProductDto.DefaultWarehouseCode)},
                        warehouse.{nameof(WarehouseSite.Name)} AS {nameof(ProductDto.DefaultWarehouseName)},

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
                    ORDER BY p.{nameof(Product.Sku)} ASC
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
