using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>提供销售订单 Detail 所需的产品 Lookup 查询。</summary>
public partial class ProductRepository
{
    /// <summary>按客户和关键词分页查询产品及客户化销售快照。</summary>
    public async Task<PagedResponseBase<SalesOrderProductLookupDto>> SearchSalesOrderProductsAsync(
        SalesOrderProductSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 30 : request.PageSize, 100);
        var parameters = new DynamicParameters(new
        {
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize
        });
        var hasCustomer = request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty;
        var keywordFilter = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            keywordFilter = hasCustomer
                ? @" AND (cp.Sku LIKE @Keyword ESCAPE '~'
                         OR cp.CustomerSku LIKE @Keyword ESCAPE '~'
                         OR cp.LinkSku LIKE @Keyword ESCAPE '~'
                         OR p.DescriptionEn LIKE @Keyword ESCAPE '~')"
                : @" AND (p.Sku LIKE @Keyword ESCAPE '~'
                         OR p.LinkSku LIKE @Keyword ESCAPE '~'
                         OR p.DescriptionEn LIKE @Keyword ESCAPE '~')";
        }

        string fromSql;
        string selectSql;
        if (hasCustomer)
        {
            parameters.Add("CustomerId", request.CustomerId!.Value);
            fromSql = @"
                FROM CustomerProduct cp WITH(NOLOCK)
                INNER JOIN Product p WITH(NOLOCK) ON p.Uid = cp.SkuId AND p.IsDeleted = 0
                LEFT JOIN ProductColor pc WITH(NOLOCK) ON pc.Uid = p.ColorId AND pc.IsDeleted = 0
                WHERE cp.CustomerId = @CustomerId AND cp.IsDeleted = 0 AND cp.IsActive = 1";
            selectSql = @"
                p.Uid AS ProductId,
                cp.Uid AS CustomerProductId,
                ISNULL(cp.CustomerSku, '') AS CustomerSku,
                ISNULL(p.Sku, '') AS Sku,
                ISNULL(cp.LinkSku, p.LinkSku) AS LinkSku,
                ISNULL(p.DescriptionEn, '') AS DefaultDescriptionEn,
                ISNULL(pc.ColorNameEn, '') AS ColorNameEn,
                ISNULL(p.FeaturesEn, '') AS FeaturesEn,
                ISNULL(p.MainImage, '') AS MainImage,
                ISNULL(cp.CNYPrice, p.CNYPrice) AS CnyPrice,
                ISNULL(cp.USDPrice, p.USDPrice) AS UsdPrice";
        }
        else
        {
            fromSql = @"
                FROM Product p WITH(NOLOCK)
                LEFT JOIN ProductColor pc WITH(NOLOCK) ON pc.Uid = p.ColorId AND pc.IsDeleted = 0
                WHERE p.IsDeleted = 0";
            selectSql = @"
                p.Uid AS ProductId,
                CAST(NULL AS uniqueidentifier) AS CustomerProductId,
                CAST(N'' AS nvarchar(200)) AS CustomerSku,
                ISNULL(p.Sku, '') AS Sku,
                ISNULL(p.LinkSku, '') AS LinkSku,
                ISNULL(p.DescriptionEn, '') AS DefaultDescriptionEn,
                ISNULL(pc.ColorNameEn, '') AS ColorNameEn,
                ISNULL(p.FeaturesEn, '') AS FeaturesEn,
                ISNULL(p.MainImage, '') AS MainImage,
                p.CNYPrice AS CnyPrice,
                p.USDPrice AS UsdPrice";
        }

        var sql = $@"
            SELECT COUNT(1) {fromSql} {keywordFilter};
            SELECT {selectSql}
            {fromSql} {keywordFilter}
            ORDER BY p.Sku, p.Uid
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var command = new CommandDefinition(
            sql,
            parameters,
            transaction: Transaction,
            cancellationToken: cancellationToken);
        using var multi = await Connection.QueryMultipleAsync(command);
        return new PagedResponseBase<SalesOrderProductLookupDto>
        {
            TotalCount = await multi.ReadFirstAsync<int>(),
            Items = (await multi.ReadAsync<SalesOrderProductLookupDto>()).ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }
}
