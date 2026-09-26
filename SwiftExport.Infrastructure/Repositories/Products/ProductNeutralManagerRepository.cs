using Dapper;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>使用 Dapper 实现跨主型号中性产品分页和唯一性读取。</summary>
public sealed class ProductNeutralManagerRepository(RepositoryBaseContext context)
    : DapperRepository<ProductNeutral>(context), IProductNeutralManagerRepository
{
    /// <summary>分页查询中性产品，并从所属主型号解析分类及描述，从颜色表解析显示名称。</summary>
    public async Task<PagedResponseBase<ProductNeutralManagerListItemDto>> GetPagedAsync(
        ProductNeutralManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new ProductNeutralManagerSearchRequest();
        var pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize,
            Limit = pageSize,
            EmptyGuid = Guid.Empty
        });
        var where = new StringBuilder("WHERE pn.IsDeleted=0 AND pm.IsDeleted=0");

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where.Append(@"
 AND (pn.ProductCode LIKE @Keyword ESCAPE '~'
   OR pm.MasterSku LIKE @Keyword ESCAPE '~'
   OR pm.DefaultDescriptionZh LIKE @Keyword ESCAPE '~'
   OR pm.DefaultDescriptionEn LIKE @Keyword ESCAPE '~')");
        }

        var categoryIds = request.CategoryIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        // 中性产品沿用所属主型号分类；未分类与真实分类组合时必须放在同一 OR 分组中。
        if (request.IncludeUncategorized && categoryIds.Length > 0)
        {
            parameters.Add("CategoryIds", categoryIds);
            where.Append(" AND (pm.CategoryId IN @CategoryIds OR pm.CategoryId=@EmptyGuid OR pm.CategoryId IS NULL)");
        }
        else if (request.IncludeUncategorized)
        {
            where.Append(" AND (pm.CategoryId=@EmptyGuid OR pm.CategoryId IS NULL)");
        }
        else if (categoryIds.Length > 0)
        {
            parameters.Add("CategoryIds", categoryIds);
            where.Append(" AND pm.CategoryId IN @CategoryIds");
        }

        var colorIds = request.ColorIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        if (colorIds.Length > 0)
        {
            parameters.Add("ColorIds", colorIds);
            where.Append(" AND pn.ColorId IN @ColorIds");
        }

        var sql = $@"
SELECT COUNT(1)
FROM dbo.ProductNeutral pn WITH(NOLOCK)
INNER JOIN dbo.ProductMaster pm WITH(NOLOCK) ON pm.Uid=pn.ProductMasterId
{where};

SELECT pn.*, pm.MasterSku, pm.DefaultDescriptionZh AS ProductMasterDescriptionZh,
       pm.CategoryId, pc.CategoryName,
       COALESCE(NULLIF(color.ColorNameZh,''), color.ColorNameEn, color.ColorCode) AS ColorName
FROM dbo.ProductNeutral pn WITH(NOLOCK)
INNER JOIN dbo.ProductMaster pm WITH(NOLOCK) ON pm.Uid=pn.ProductMasterId
LEFT JOIN dbo.ProductCategory pc WITH(NOLOCK) ON pc.Uid=pm.CategoryId AND pc.IsDeleted=0
LEFT JOIN dbo.ProductColor color WITH(NOLOCK) ON color.Uid=pn.ColorId AND color.IsDeleted=0
{where}
ORDER BY pn.ProductCode, pn.Uid
OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, Transaction, cancellationToken: cancellationToken));
        var total = await multi.ReadFirstAsync<int>();
        var rows = await multi.ReadAsync<ProductNeutralManagerProjection>();
        return new PagedResponseBase<ProductNeutralManagerListItemDto>
        {
            Items = rows.Select(ToListItem).ToList(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    /// <summary>对规范化产品编码执行集合查询，并按调用场景决定是否申请更新锁。</summary>
    public async Task<IReadOnlyList<ProductNeutral>> GetByProductCodesAsync(
        IEnumerable<string> productCodes,
        bool withUpdLock = false,
        CancellationToken cancellationToken = default)
    {
        var values = productCodes.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length == 0) return [];
        var sql = $"SELECT * FROM dbo.ProductNeutral {GetLockHint(withUpdLock)} WHERE ProductCode IN @Values AND IsDeleted=0;";
        var rows = await Connection.QueryAsync<ProductNeutral>(
            new CommandDefinition(sql, new { Values = values }, Transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private static ProductNeutralManagerListItemDto ToListItem(ProductNeutralManagerProjection row) => new()
    {
        Uid = row.Uid,
        Version = row.Version,
        IsDeleted = row.IsDeleted,
        CreatedDate = row.CreatedDate,
        CreatedBy = row.CreatedBy,
        UpdatedDate = row.UpdatedDate,
        UpdatedBy = row.UpdatedBy,
        MasterSku = row.MasterSku,
        ProductMasterDescriptionZh = row.ProductMasterDescriptionZh,
        CategoryId = row.CategoryId,
        CategoryName = row.CategoryName,
        ColorName = row.ColorName,
        Data = new ProductNeutralManagerWriteDto
        {
            ProductMasterId = row.ProductMasterId,
            ProductCode = row.ProductCode,
            ColorId = row.ColorId,
            DefaultSalesPriceUsd = row.DefaultSalesPriceUsd,
            DefaultExchangeRate = row.DefaultExchangeRate,
            ProfitMargin = row.ProfitMargin,
            DefaultPurchasePriceCny = row.DefaultPurchasePriceCny,
            ImagePath = row.ImagePath
        }
    };

    private sealed class ProductNeutralManagerProjection : ProductNeutral
    {
        public string? MasterSku { get; set; }
        public string? ProductMasterDescriptionZh { get; set; }
        public Guid CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? ColorName { get; set; }
    }
}
