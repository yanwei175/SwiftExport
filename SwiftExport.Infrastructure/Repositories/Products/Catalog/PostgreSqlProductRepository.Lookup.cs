using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>产品轻量候选保持精确、前缀、包含匹配优先级，客户关系通过 EXISTS 避免重复。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>关键词 Trim，最多五十条；百分号下划线仍是裸 LIKE 通配符，方括号字符组跨库差异待主控决定。</summary>
    public Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request) => SearchLookupAsync(request, CancellationToken.None);

    /// <summary>保留候选筛选与排序，把替代型搜索令牌传递到数据库。</summary>
    public async Task<IReadOnlyList<ProductLookupDto>> SearchLookupAsync(ProductLookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var keyword = request.Keyword?.Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return [];
        var materialType = request.MaterialType?.Trim();
        var typeFilter = string.IsNullOrWhiteSpace(materialType) ? "" : " AND rtrim(p.material_type,' ') COLLATE public.erp_dictionary_ci=@MaterialType";
        // 包材身份候选不能由调用方关闭启用限制；普通候选省略新参数时保持原语义。
        var activeFilter = request.ActiveOnly || materialType == MaterialTypesEnum.包材 ? " AND p.is_active=TRUE" : "";
        var parameters = new
        {
            Limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 50),
            request.ExcludeProductId,
            MaterialType = materialType,
            ExactKeyword = keyword,
            PrefixKeyword = $"{keyword}%",
            ContainsKeyword = $"%{keyword}%",
            EmptyGuid = Guid.Empty
        };
        var category = """
            CASE WHEN pm.uid IS NULL OR pm.category_id IS NULL OR pm.category_id=@EmptyGuid THEN '未分类'
                 ELSE COALESCE(pat.category_name,'分类已失效') END AS "CategoryName"
            """;
        var contains = string.Join(" OR ", new[] { "p.sku", "p.description_en", "p.description_zh", "p.link_sku", "pat.category_name" }
            .Select(column => $"{column} COLLATE public.erp_dictionary_ci LIKE @ContainsKeyword ESCAPE ''"));
        // 普通候选默认不加 IsActive 条件；显式类型/启用过滤只收窄本次请求，不改变客户关系资格。
        var rows = await Connection.QueryAsync<ProductLookupDto>(new CommandDefinition($"""
            SELECT p."uid" AS "Uid",p."sku" AS "Sku",p."description_en" AS "DescriptionEn",p."description_zh" AS "DescriptionZh",p."link_sku" AS "LinkSku",p."main_image" AS "MainImage",p."features_en" AS "FeaturesEn",p."features_zh" AS "FeaturesZh",p."material_type" AS "MaterialType",p."unit" AS "Unit",p."usd_price" AS "USDPrice",p."cny_price" AS "CNYPrice",
                {category},pc.color_name_zh AS "Finish"
            FROM public.product p
            LEFT JOIN public.product_master pm ON pm.uid=p.product_master_id AND (pm.is_deleted=FALSE OR pm.is_deleted IS NULL)
            LEFT JOIN public.product_category pat ON pat.uid=pm.category_id AND (pat.is_deleted=FALSE OR pat.is_deleted IS NULL)
            LEFT JOIN public.product_color pc ON pc.uid=p.color_id AND (pc.is_deleted=FALSE OR pc.is_deleted IS NULL)
            WHERE (p.is_deleted=FALSE OR p.is_deleted IS NULL) AND (@ExcludeProductId=@EmptyGuid OR p.uid<>@ExcludeProductId)
                {typeFilter}{activeFilter}
                AND ({contains} OR EXISTS (SELECT 1 FROM public.customer_product cp WHERE cp.sku_id=p.uid
                    AND (cp.is_deleted=FALSE OR cp.is_deleted IS NULL) AND cp.customer_sku COLLATE public.erp_dictionary_ci LIKE @ContainsKeyword ESCAPE ''))
            ORDER BY CASE
                WHEN {CatalogEqual("p.sku", "@ExactKeyword")} THEN 0
                WHEN EXISTS (SELECT 1 FROM public.customer_product exactCp WHERE exactCp.sku_id=p.uid
                    AND (exactCp.is_deleted=FALSE OR exactCp.is_deleted IS NULL) AND {CatalogEqual("exactCp.customer_sku", "@ExactKeyword")}) THEN 1
                WHEN p.sku COLLATE public.erp_dictionary_ci LIKE @PrefixKeyword ESCAPE '' THEN 2
                WHEN EXISTS (SELECT 1 FROM public.customer_product prefixCp WHERE prefixCp.sku_id=p.uid
                    AND (prefixCp.is_deleted=FALSE OR prefixCp.is_deleted IS NULL) AND prefixCp.customer_sku COLLATE public.erp_dictionary_ci LIKE @PrefixKeyword ESCAPE '') THEN 3
                ELSE 4 END,p.sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,p.uid LIMIT @Limit
            """, parameters, Transaction, cancellationToken: cancellationToken));
        return rows.ToArray();
    }
}

