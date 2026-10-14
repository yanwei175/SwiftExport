using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.BomManagement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.BomManagement;

/// <summary>以 PostgreSQL 参数化查询读取成品和共享 BOM 摘要，聚合组成后再联表避免计数放大。</summary>
public sealed class PostgreSqlBomManagementQueryRepository(RepositoryBaseContext context) : IBomManagementQueryRepository
{
    // 编号归组保持现有 ICU 和尾部 ASCII 空格等值规则，正标记只表达有效性。
    private const string Bound = "p.bom_version>0 AND p.bom_no IS NOT NULL AND btrim(p.bom_no,' ')<>''";
    private const string Groups = """
        WITH parents AS (
            SELECT rtrim(bom_no,' ') COLLATE public.erp_dictionary_ci AS code,count(*)::integer AS parent_count
            FROM public.product WHERE is_deleted=FALSE AND bom_version>0 AND bom_no IS NOT NULL AND btrim(bom_no,' ')<>''
            GROUP BY rtrim(bom_no,' ') COLLATE public.erp_dictionary_ci
        ), components AS (
            SELECT rtrim(bom_no,' ') COLLATE public.erp_dictionary_ci AS code,
                count(*)::integer AS stored_count,count(*) FILTER(WHERE bom_version>0)::integer AS current_count,
                bool_or(bom_version<=0) AS has_invalid
            FROM public.bom WHERE is_deleted=FALSE GROUP BY rtrim(bom_no,' ') COLLATE public.erp_dictionary_ci
        )
        """;

    /// <inheritdoc />
    public async Task<PagedResponseBase<BomProductListItemDto>> SearchProductsAsync(BomProductSearchRequest request, CancellationToken cancellationToken)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (request.PageIndex - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);
        parameters.Add("EmptyGuid", Guid.Empty);
        var filters = new List<string> { "p.is_deleted=FALSE", "p.material_type='成品'" };
        if (request.IsBound.HasValue) filters.Add(request.IsBound.Value ? $"({Bound})" : $"NOT COALESCE(({Bound}),FALSE)");
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            var columns = new[] { "p.sku", "pm.master_sku", "p.bom_no", "p.description_en", "p.description_zh" };
            filters.Add("(" + string.Join(" OR ", columns.Select(column => $"{column} COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'")) + ")");
        }
        if (request.CategoryIds?.Count > 0)
        {
            parameters.AddIds("CategoryIds", request.CategoryIds.Distinct().ToArray());
            filters.Add("COALESCE(pm.category_id,@EmptyGuid)=ANY(@CategoryIds)");
        }
        if (request.ColorIds?.Count > 0)
        {
            parameters.AddIds("ColorIds", request.ColorIds.Distinct().ToArray());
            filters.Add("p.color_id=ANY(@ColorIds)");
        }
        var from = $"""
            FROM public.product p
            LEFT JOIN public.product_master pm ON pm.uid=p.product_master_id AND pm.is_deleted=FALSE
            LEFT JOIN public.product_category category ON category.uid=pm.category_id AND category.is_deleted=FALSE
            LEFT JOIN public.product_color color ON color.uid=p.color_id AND color.is_deleted=FALSE
            LEFT JOIN parents ON parents.code=rtrim(p.bom_no,' ') COLLATE public.erp_dictionary_ci
            LEFT JOIN components ON components.code=rtrim(p.bom_no,' ') COLLATE public.erp_dictionary_ci
            WHERE {string.Join(" AND ", filters)}
            """;
        // 一条命令取得计数与页数据；只读不引入全图锁或修改业务事务。
        var sql = $"""
            {Groups} SELECT count(*)::integer {from};
            {Groups} SELECT p.uid AS "Uid",p.version AS "Version",pm.master_sku AS "MasterSku",p.sku AS "Sku",
                p.description_en AS "DescriptionEn",p.description_zh AS "DescriptionZh",p.main_image AS "MainImage",
                color.color_name_zh AS "Finish",COALESCE(pm.category_id,@EmptyGuid) AS "CategoryId",
                CASE WHEN pm.category_id IS NULL OR pm.category_id=@EmptyGuid THEN '未分类'
                    ELSE COALESCE(category.category_name,'分类已失效') END AS "CategoryName",
                p.supply_type AS "SupplyType",p.unit AS "Unit",p.bom_no AS "BomNo",p.bom_version AS "BomVersion",
                COALESCE(({Bound}),FALSE) AS "IsBound",
                CASE WHEN {Bound} THEN COALESCE(components.current_count,0) ELSE 0 END AS "ComponentCount",
                CASE WHEN {Bound} THEN COALESCE(parents.parent_count,0) ELSE 0 END AS "SharedProductCount"
            {from} ORDER BY p.sku COLLATE public.erp_dictionary_ci,p.uid OFFSET @Offset LIMIT @PageSize;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var count = await reader.ReadSingleAsync<int>();
        var rows = (await reader.ReadAsync<BomProductListItemDto>()).ToList();
        return new() { Items = rows, TotalCount = count, PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc />
    public Task<BomNumberInfoDto> GetNumberInfoAsync(string bomNo, CancellationToken cancellationToken)
    {
        var sql = $"""
            {Groups} SELECT @BomNo AS "BomNo",COALESCE(parents.parent_count,0) AS "BoundProductCount",
                CASE WHEN parents.parent_count>0 THEN COALESCE(components.current_count,0) ELSE 0 END AS "ComponentCount",
                COALESCE(components.stored_count,0) AS "StoredComponentCount",COALESCE(components.has_invalid,FALSE) AS "HasInvalidComponents"
            FROM (SELECT rtrim(CAST(@BomNo AS text),' ') COLLATE public.erp_dictionary_ci AS code) requested
            LEFT JOIN parents ON parents.code=requested.code LEFT JOIN components ON components.code=requested.code
            """;
        return context.Session.Connection.QuerySingleAsync<BomNumberInfoDto>(new CommandDefinition(sql, new { BomNo = bomNo.Trim() }, context.Session.Transaction, cancellationToken: cancellationToken));
    }
}
