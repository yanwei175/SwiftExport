using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>销售订单产品选择投影，保留客户价格覆盖和现有活动关系范围，不新增业务锁。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>在当前连接事务读取产品及客户快照候选；普通产品仍包含停用项，资格由原业务入口判断。</summary>
    public async Task<PagedResponseBase<SalesOrderProductLookupDto>> SearchSalesOrderProductsAsync(
        SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 30 : request.PageSize, 100);
        var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
        var hasCustomer = request.CustomerId.HasValue && request.CustomerId.Value != Guid.Empty;
        var keywordFilter = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            keywordFilter = hasCustomer
                ? " AND (cp.sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR cp.customer_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR cp.link_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR p.description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')"
                : " AND (p.sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR p.link_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR p.description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')";
        }

        string from;
        string projection;
        if (hasCustomer)
        {
            parameters.Add("CustomerId", request.CustomerId!.Value);
            from = """
                FROM public.customer_product cp
                JOIN public.product p ON p.uid=cp.sku_id AND p.is_deleted=FALSE
                LEFT JOIN public.product_color pc ON pc.uid=p.color_id AND pc.is_deleted=FALSE
                WHERE cp.customer_id=@CustomerId AND cp.is_deleted=FALSE AND cp.is_active=TRUE
                """;
            projection = """
                cp.uid AS "CustomerProductId", coalesce(cp.customer_sku,'') AS "CustomerSku",
                coalesce(cp.link_sku,p.link_sku) AS "LinkSku",
                coalesce(cp.cny_price,p.cny_price) AS "CnyPrice", coalesce(cp.usd_price,p.usd_price) AS "UsdPrice"
                """;
        }
        else
        {
            from = """
                FROM public.product p
                LEFT JOIN public.product_color pc ON pc.uid=p.color_id AND pc.is_deleted=FALSE
                WHERE p.is_deleted=FALSE
                """;
            projection = """
                NULL::uuid AS "CustomerProductId", ''::text AS "CustomerSku",
                coalesce(p.link_sku,'') AS "LinkSku", p.cny_price AS "CnyPrice", p.usd_price AS "UsdPrice"
                """;
        }
        // 两条读取沿用原接口的一般读已提交行为；不声称 COUNT 与数据具有跨语句快照。
        // 同一产品可以有多条客户关系，最后追加关系 Uid 保证分页顺序稳定，不合并独立候选。
        var order = hasCustomer ? ",cp.uid" : string.Empty;
        var sql = $"""
            SELECT count(*)::integer {from} {keywordFilter};
            SELECT p.uid AS "ProductId", coalesce(p.sku,'') AS "Sku",
                   coalesce(p.description_en,'') AS "DefaultDescriptionEn",
                   coalesce(pc.color_name_en,'') AS "ColorNameEn", coalesce(p.features_en,'') AS "FeaturesEn",
                   coalesce(p.main_image,'') AS "MainImage", {projection}
            {from} {keywordFilter}
            ORDER BY p.sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,p.uid{order}
            LIMIT @PageSize OFFSET @Offset;
            """;
        using var result = await Connection.QueryMultipleAsync(new CommandDefinition(sql, parameters,
            transaction: Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<SalesOrderProductLookupDto>
        {
            TotalCount = await result.ReadFirstAsync<int>(), Items = (await result.ReadAsync<SalesOrderProductLookupDto>()).ToList(),
            PageIndex = pageIndex, PageSize = pageSize
        };
    }
}
