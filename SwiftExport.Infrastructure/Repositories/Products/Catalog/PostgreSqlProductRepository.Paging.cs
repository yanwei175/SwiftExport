using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>产品分页投影当前分类、供应商、仓库和包装；客户条件独立 AND，不产生关系重复行。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>保留原筛选与二十条默认、五百条上限；普通查询不锁行，包装保持全部类型的原优先级。</summary>
    public async Task<PagedResponseBase<ProductDto>> GetPagedAsync(ProductSearchRequest request)
    {
        request ??= new ProductSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 500);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("Limit", pageSize);
        parameters.Add("EmptyGuid", Guid.Empty);
        var filters = new List<string> { "(p.is_deleted=FALSE OR p.is_deleted IS NULL)" };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            filters.Add("(" + string.Join(" OR ", new[]
                { "p.sku", "p.description_en", "p.description_zh", "pc.color_code", "pc.color_name_en", "pc.color_name_zh", "pat.category_name" }
                .Select(column => $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'")) + ")");
        }
        var batch = request.BatchKeywords?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(300).ToArray() ?? [];
        if (batch.Length > 0)
        {
            parameters.AddTexts("BatchKeywords", batch);
            filters.Add($"({CatalogTextSet("p.sku", "BatchKeywords")} OR {CatalogTextSet("pc.color_code", "BatchKeywords")})");
        }
        if (!string.IsNullOrWhiteSpace(request.ColorCodeUnique))
        {
            parameters.Add("ColorCodeUnique", request.ColorCodeUnique.Trim());
            filters.Add(CatalogEqual("pc.color_code_unique", "@ColorCodeUnique"));
        }
        var materialTypes = request.MaterialTypeList?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (materialTypes.Length > 0)
        {
            parameters.AddTexts("MaterialTypeList", materialTypes);
            filters.Add(CatalogTextSet("p.material_type", "MaterialTypeList"));
        }
        if (!string.IsNullOrWhiteSpace(request.CategoryName))
        {
            // 此入口原本是裸 LIKE，不调用 Build；方括号字符组差异由主控统一决定。
            parameters.Add("CategoryName", $"%{request.CategoryName.Trim()}%");
            filters.Add("pat.category_name COLLATE public.erp_dictionary_ci LIKE @CategoryName ESCAPE ''");
        }
        if (request.CategoryId != Guid.Empty)
        {
            parameters.Add("CategoryId", request.CategoryId);
            filters.Add("pm.category_id=@CategoryId");
        }
        var categoryIds = request.CategoryIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        var colorIds = request.ColorIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        var customerIds = request.CustomerIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];
        // 多组身份通过已有参数包显式绑定 uuid 数组，避免 Dapper 展开及隐式类型推断。
        if (categoryIds.Length > 0) { parameters.AddIds("CategoryIds", categoryIds); filters.Add("pm.category_id=ANY(@CategoryIds)"); }
        if (colorIds.Length > 0) { parameters.AddIds("ColorIds", colorIds); filters.Add("pc.uid=ANY(@ColorIds)"); }
        if (request.CustomerId != Guid.Empty)
        {
            parameters.Add("CustomerId", request.CustomerId);
            filters.Add("EXISTS (SELECT 1 FROM public.customer_product cp WHERE cp.sku_id=p.uid AND cp.customer_id=@CustomerId AND (cp.is_deleted=FALSE OR cp.is_deleted IS NULL))");
        }
        if (customerIds.Length > 0)
        {
            parameters.AddIds("CustomerIds", customerIds);
            filters.Add("EXISTS (SELECT 1 FROM public.customer_product cp WHERE cp.sku_id=p.uid AND cp.customer_id=ANY(@CustomerIds) AND (cp.is_deleted=FALSE OR cp.is_deleted IS NULL))");
        }
        var codes = request.CustomerCodes?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (codes.Length > 0)
        {
            parameters.AddTexts("CustomerCodes", codes);
            filters.Add($"""
                EXISTS (SELECT 1 FROM public.customer_product cp INNER JOIN public.customer c ON c.uid=cp.customer_id
                    WHERE cp.sku_id=p.uid AND (cp.is_deleted=FALSE OR cp.is_deleted IS NULL)
                    AND (c.is_deleted=FALSE OR c.is_deleted IS NULL) AND {CatalogTextSet("c.customer_code", "CustomerCodes")})
                """);
        }
        var from = $"FROM public.product p {CatalogQueryJoins} {CatalogReferenceJoins}";
        var where = "WHERE " + string.Join(" AND ", filters);
        // 分开计数和取页，沿用同一连接事务和筛选；不通过一对多客户 JOIN 放大计数。
        var count = await Connection.ExecuteScalarAsync<int>($"SELECT count(*) {from} {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<ProductDto>($"""
            SELECT {CatalogDtoColumns},{CatalogClassificationColumns},{CatalogReferenceColumns},pc.color_name_zh AS "Finish",
                pat.fake_name AS "FakeName",pc.color_code_unique AS "ColorCodeUnique",pc.color_code AS "ColorCode",pc.color_name_en AS "ColorNameEn",
                pkg.pcs_per_package AS "PcsOfCarton",pkg.nw_kg AS "NwOfCarton",pkg.gw_kg AS "GwOfCarton",pkg.cbm AS "CbmOfCarton"
            {from}
            LEFT JOIN LATERAL (SELECT pp.pcs_per_package,pp.nw_kg,pp.gw_kg,pp.cbm FROM public.product_package pp
                WHERE pp.product_id=p.uid AND (pp.is_deleted=FALSE OR pp.is_deleted IS NULL)
                ORDER BY pp.is_default DESC NULLS LAST,pp.pcs_per_package DESC NULLS LAST,pp.uid LIMIT 1) pkg ON TRUE
            {where} ORDER BY p.sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,p.uid LIMIT @Limit OFFSET @Offset
            """, parameters, Transaction);
        return new PagedResponseBase<ProductDto> { TotalCount = count, Items = rows.ToList(), PageIndex = pageIndex, PageSize = pageSize };
    }

}
