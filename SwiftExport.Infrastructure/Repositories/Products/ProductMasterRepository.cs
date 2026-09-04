using Dapper;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>使用 Dapper 持久化主型号，并提供不受一对多明细影响的稳定分页投影。</summary>
public sealed class ProductMasterRepository(RepositoryBaseContext context)
    : DapperRepository<ProductMaster>(context), IProductMasterRepository
{
    /// <summary>按主型号字段和中性产品编码查询，使用 EXISTS 避免分页重复。</summary>
    public async Task<PagedResponseBase<ProductMasterListItemDto>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default)
    {
        request ??= new ProductMasterSearchRequest();
        var pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, Limit = pageSize });
        var where = new StringBuilder("WHERE pm.IsDeleted = 0");

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where.Append(@"
 AND (pm.MasterSku LIKE @Keyword ESCAPE '~'
   OR pm.DefaultDescriptionEn LIKE @Keyword ESCAPE '~'
   OR pm.DefaultDescriptionZh LIKE @Keyword ESCAPE '~'
   OR pm.ProductType LIKE @Keyword ESCAPE '~'
   OR pm.ProductTag LIKE @Keyword ESCAPE '~'
   OR EXISTS (SELECT 1 FROM dbo.ProductNeutral pn WITH(NOLOCK)
              WHERE pn.ProductMasterId = pm.Uid AND pn.IsDeleted = 0
                AND pn.ProductCode LIKE @Keyword ESCAPE '~'))");
        }

        var categoryIds = request.CategoryIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        if (categoryIds.Length > 0)
        {
            parameters.Add("CategoryIds", categoryIds);
            where.Append(" AND pm.CategoryId IN @CategoryIds");
        }

        var sql = $@"
SELECT COUNT(1) FROM dbo.ProductMaster pm WITH(NOLOCK) {where};
SELECT pm.*, pc.CategoryName, s.SupplierName AS DefaultSupplierName,
       mainImage.ImagePath AS MainImagePath,
       mainImage.DefaultSalesPriceUsd,
       mainImage.DefaultPurchasePriceCny
FROM dbo.ProductMaster pm WITH(NOLOCK)
LEFT JOIN dbo.ProductCategory pc WITH(NOLOCK) ON pc.Uid=pm.CategoryId AND pc.IsDeleted=0
LEFT JOIN dbo.Supplier s WITH(NOLOCK) ON s.Uid=pm.DefaultSupplierId AND s.IsDeleted=0
OUTER APPLY
(
    SELECT TOP (1) pn.ImagePath, pn.DefaultSalesPriceUsd, pn.DefaultPurchasePriceCny
    FROM dbo.ProductNeutral pn WITH(NOLOCK)
    WHERE pn.ProductMasterId=pm.Uid
      AND pn.IsDeleted=0
      AND pn.IsMainImage=1
    ORDER BY pn.ProductCode, pn.Uid
) mainImage
{where}
ORDER BY pm.MasterSku, pm.Uid
OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";
        using var multi = await Connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, Transaction, cancellationToken: cancellationToken));
        var total = await multi.ReadFirstAsync<int>();
        var rows = await multi.ReadAsync<ProductMasterProjection>();
        return new PagedResponseBase<ProductMasterListItemDto>
        {
            Items = rows.Select(ToListItem).ToList(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    /// <summary>读取详情头部；事务校验场景可申请更新锁。</summary>
    public async Task<ProductMasterDetailDto?> GetDetailHeaderAsync(Guid productMasterId, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var sql = $@"
SELECT pm.*, pc.CategoryName, s.SupplierName AS DefaultSupplierName
FROM dbo.ProductMaster pm {GetLockHint(withUpdLock)}
LEFT JOIN dbo.ProductCategory pc WITH(NOLOCK) ON pc.Uid=pm.CategoryId AND pc.IsDeleted=0
LEFT JOIN dbo.Supplier s WITH(NOLOCK) ON s.Uid=pm.DefaultSupplierId AND s.IsDeleted=0
WHERE pm.Uid=@ProductMasterId AND pm.IsDeleted=0;";
        var row = await Connection.QuerySingleOrDefaultAsync<ProductMasterProjection>(
            new CommandDefinition(sql, new { ProductMasterId = productMasterId }, Transaction, cancellationToken: cancellationToken));
        return row is null ? null : ToDetail(row);
    }

    /// <summary>对规范化编码执行集合查询，供应用层识别当前占用者。</summary>
    public async Task<IReadOnlyList<ProductMaster>> GetByMasterSkusAsync(IEnumerable<string> masterSkus, bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var values = masterSkus.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        var sql = $"SELECT * FROM dbo.ProductMaster {GetLockHint(withUpdLock)} WHERE MasterSku IN @Values AND IsDeleted=0;";
        var rows = await Connection.QueryAsync<ProductMaster>(new CommandDefinition(sql, new { Values = values }, Transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private static ProductMasterListItemDto ToListItem(ProductMasterProjection row) => new()
    {
        Uid = row.Uid, Version = row.Version, IsDeleted = row.IsDeleted, CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy, UpdatedDate = row.UpdatedDate, UpdatedBy = row.UpdatedBy,
        CategoryName = row.CategoryName, DefaultSupplierName = row.DefaultSupplierName,
        MainImagePath = row.MainImagePath,
        DefaultSalesPriceUsd = row.DefaultSalesPriceUsd,
        DefaultPurchasePriceCny = row.DefaultPurchasePriceCny,
        Data = ToWriteDto(row)
    };

    private static ProductMasterDetailDto ToDetail(ProductMasterProjection row) => new()
    {
        Uid = row.Uid, Version = row.Version, IsDeleted = row.IsDeleted, CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy, UpdatedDate = row.UpdatedDate, UpdatedBy = row.UpdatedBy,
        CategoryName = row.CategoryName, DefaultSupplierName = row.DefaultSupplierName,
        Master = ToWriteDto(row)
    };

    private static ProductMasterWriteDto ToWriteDto(ProductMaster row) => new()
    {
        MasterSku = row.MasterSku, DefaultDescriptionEn = row.DefaultDescriptionEn,
        DefaultDescriptionZh = row.DefaultDescriptionZh, DefaultFeaturesEn = row.DefaultFeaturesEn,
        DefaultFeaturesZh = row.DefaultFeaturesZh, DefaultPcsPerPackage = row.DefaultPcsPerPackage,
        DefaultNetWeight = row.DefaultNetWeight, DefaultGrossWeight = row.DefaultGrossWeight,
        DefaultCbm = row.DefaultCbm, ProductType = row.ProductType, ProductTag = row.ProductTag,
        CategoryId = row.CategoryId, DefaultSupplierId = row.DefaultSupplierId
    };

    private sealed class ProductMasterProjection : ProductMaster
    {
        public string? CategoryName { get; set; }
        public string? DefaultSupplierName { get; set; }
        public string? MainImagePath { get; set; }
        public decimal? DefaultSalesPriceUsd { get; set; }
        public decimal? DefaultPurchasePriceCny { get; set; }
    }
}
