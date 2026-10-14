using Dapper;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Suppliers.SupplierProducts;

/// <summary>供应商产品 PostgreSQL 读取适配；保留独立资料、实时展示与采购快照边界，复用外层事务及通用 CRUD。</summary>
public sealed class PostgreSqlSupplierProductRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<SupplierProduct>(context), ISupplierProductRepository
{
    private const string EntityColumns = """
        sp."uid" AS "Uid",
        sp."version" AS "Version",
        sp."is_deleted" AS "IsDeleted",
        sp."created_date" AS "CreatedDate",
        sp."created_by" AS "CreatedBy",
        sp."updated_date" AS "UpdatedDate",
        sp."updated_by" AS "UpdatedBy",
        sp."supplier_id" AS "SupplierId",
        sp."supplier_code" AS "SupplierCode",
        sp."sku_id" AS "SkuId",
        sp."product_sku" AS "ProductSku",
        sp."supplier_sku" AS "SupplierSku",
        sp."supplier_sku_description_zh" AS "SupplierSkuDescriptionZh",
        sp."supplier_sku_features_zh" AS "SupplierSkuFeaturesZh",
        sp."unit_price" AS "UnitPrice",
        sp."tax_price" AS "TaxPrice"
    """;
    private const string Projection = """
        sp.uid AS "Uid",sp.version AS "Version",sp.is_deleted AS "IsDeleted",
        sp.created_date AS "CreatedDate",sp.created_by AS "CreatedBy",sp.updated_date AS "UpdatedDate",sp.updated_by AS "UpdatedBy",
        sp.supplier_id AS "SupplierId",s.supplier_code AS "SupplierCode",s.supplier_name AS "SupplierName",
        sp.sku_id AS "SkuId",p.sku AS "ProductSku",p.link_sku AS "LinkSku",
        p.description_en AS "DescriptionEn",p.description_zh AS "DescriptionZh",p.features_zh AS "FeaturesZh",
        CASE WHEN p.uid IS NULL THEN ''
             WHEN pm.uid IS NULL OR pm.category_id IS NULL OR pm.category_id=@EmptyGuid THEN '未分类'
             ELSE coalesce(pc.category_name,'分类已失效') END AS "CategoryName",
        p.material_type AS "MaterialType",p.main_image AS "MainImage",
        sp.supplier_sku AS "SupplierSku",sp.supplier_sku_description_zh AS "SupplierSkuDescriptionZh",
        sp.supplier_sku_features_zh AS "SupplierSkuFeaturesZh",sp.unit_price AS "UnitPrice",sp.tax_price AS "TaxPrice",
        s.currency AS "Currency",s.tax_rate AS "TaxRate",
        CASE WHEN p.default_supplier_id=sp.supplier_id THEN TRUE ELSE FALSE END AS "IsProductDefaultSupplier"
        """;
    private const string From = """
        FROM public.supplier_product sp
        JOIN public.supplier s ON s.uid=sp.supplier_id AND (s.is_deleted=FALSE OR s.is_deleted IS NULL)
        LEFT JOIN public.product p ON p.uid=sp.sku_id AND (p.is_deleted=FALSE OR p.is_deleted IS NULL)
        LEFT JOIN public.product_master pm ON pm.uid=p.product_master_id AND (pm.is_deleted=FALSE OR pm.is_deleted IS NULL)
        LEFT JOIN public.product_category pc ON pc.uid=pm.category_id AND (pc.is_deleted=FALSE OR pc.is_deleted IS NULL)
        """;

    /// <summary>读取当前联表显示值；供应商失效隐藏关系，可空或失效 Product 不隐藏独立资料，不额外锁联表。</summary>
    public Task<SupplierProductDto?> GetDtoByIdAsync(Guid uid) => Connection.QueryFirstOrDefaultAsync<SupplierProductDto>(
        $"SELECT {Projection} {From} WHERE sp.uid=@Uid AND (sp.is_deleted=FALSE OR sp.is_deleted IS NULL)",
        new { Uid = uid, EmptyGuid = Guid.Empty }, transaction: Transaction);

    /// <summary>限定所属供应商分页，保留原关键词与默认 20/上限 200；两次读取不声称具有跨语句快照。</summary>
    public async Task<PagedResponseBase<SupplierProductDto>> GetPagedAsync(SupplierProductSearchRequest request)
    {
        request ??= new SupplierProductSearchRequest();
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new DynamicParameters(new { request.SupplierId, EmptyGuid = Guid.Empty, Offset = (index - 1) * size, Limit = size });
        var where = "WHERE (sp.is_deleted=FALSE OR sp.is_deleted IS NULL) AND sp.supplier_id=@SupplierId";
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            where += """
                 AND (sp.supplier_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR p.sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR p.link_sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR sp.supplier_sku_description_zh COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR sp.supplier_sku_features_zh COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')
                """;
        }
        var count = await Connection.ExecuteScalarAsync<int>($"SELECT count(*)::integer {From} {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SupplierProductDto>($"""
            SELECT {Projection} {From} {where}
            ORDER BY sp.supplier_sku COLLATE public.erp_dictionary_ci ASC NULLS FIRST,sp.uid LIMIT @Limit OFFSET @Offset
            """, parameters, Transaction);
        return new PagedResponseBase<SupplierProductDto> { Items = rows.ToList(), TotalCount = count, PageIndex = index, PageSize = size };
    }

    /// <summary>查询同供应商有效料号占用，null/空集合不匹配，输入原文本不 Trim；仅比较尾部 ASCII 空格与 ICU，返回全部原码。</summary>
    public async Task<IReadOnlyList<string>> GetExistingSupplierSkuListAsync(IEnumerable<string> supplierSkus, Guid supplierId)
    {
        var values = supplierSkus?.ToArray() ?? [];
        if (values.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.Add("SupplierId", supplierId);
        parameters.AddTexts("Codes", values);
        return (await Connection.QueryAsync<string>("""
            SELECT sp.supplier_sku FROM public.supplier_product sp
            WHERE sp.supplier_id=@SupplierId AND (sp.is_deleted=FALSE OR sp.is_deleted IS NULL)
              AND EXISTS (SELECT 1 FROM unnest(@Codes) requested(code)
                WHERE rtrim(sp.supplier_sku,' ') COLLATE public.erp_dictionary_ci=rtrim(requested.code,' ') COLLATE public.erp_dictionary_ci)
            """, parameters, Transaction)).AsList();
    }

    /// <summary>排除整批修改 UID 后查询占用；按原比较语义去重且返回一个原码，不改输入 Trim 口径或加入更新锁。</summary>
    public async Task<IReadOnlyList<string>> GetSupplierSkusUsedByOtherRecordsAsync(Guid supplierId, IEnumerable<(Guid Uid, string SupplierSku)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var values = items.ToArray();
        if (values.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.Add("SupplierId", supplierId);
        parameters.AddTexts("Codes", values.Select(item => item.SupplierSku));
        parameters.AddIds("ExcludedIds", values.Select(item => item.Uid));
        return (await Connection.QueryAsync<string>("""
            SELECT DISTINCT ON (rtrim(sp.supplier_sku,' ') COLLATE public.erp_dictionary_ci) sp.supplier_sku
            FROM public.supplier_product sp
            WHERE sp.supplier_id=@SupplierId AND NOT (sp.uid=ANY(@ExcludedIds)) AND (sp.is_deleted=FALSE OR sp.is_deleted IS NULL)
              AND EXISTS (SELECT 1 FROM unnest(@Codes) requested(code)
                WHERE rtrim(sp.supplier_sku,' ') COLLATE public.erp_dictionary_ci=rtrim(requested.code,' ') COLLATE public.erp_dictionary_ci)
            ORDER BY rtrim(sp.supplier_sku,' ') COLLATE public.erp_dictionary_ci,sp.uid
            """, parameters, Transaction)).AsList();
    }

    /// <summary>为供应商删除预检返回存在有效资料的身份；未关联 Product 的独立资料也阻止删除，不增加竞争保护。</summary>
    public async Task<IReadOnlyList<Guid>> GetSupplierIdsWithProductsAsync(IEnumerable<Guid> supplierIds)
    {
        ArgumentNullException.ThrowIfNull(supplierIds);
        var ids = supplierIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await Connection.QueryAsync<Guid>("""
            SELECT DISTINCT sp.supplier_id FROM public.supplier_product sp
            WHERE sp.supplier_id=ANY(@Uids) AND (sp.is_deleted=FALSE OR sp.is_deleted IS NULL)
            """, PostgreSqlParameters.Ids(ids), Transaction)).AsList();
    }

    /// <summary>按真实产品和供应商成对查询采购快照；保持 500 分块及全部匹配，重复资料交 Application 判断。</summary>
    public async Task<IReadOnlyList<SupplierProduct>> GetPurchaseDataAsync(IEnumerable<(Guid ProductId, Guid SupplierId)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var values = keys.Where(item => item.ProductId != Guid.Empty && item.SupplierId != Guid.Empty).Distinct().ToArray();
        if (values.Length == 0) return [];
        var rows = new List<SupplierProduct>();
        foreach (var chunk in values.Chunk(500))
        {
            var parameters = new PostgreSqlParameters();
            // 两个数组位置一一对应，不能拆成两个 ANY 条件造成跨供应商误匹配。
            parameters.AddIds("ProductIds", chunk.Select(item => item.ProductId));
            parameters.AddIds("SupplierIds", chunk.Select(item => item.SupplierId));
            rows.AddRange(await Connection.QueryAsync<SupplierProduct>($"""
                SELECT {EntityColumns} FROM public.supplier_product sp
                JOIN unnest(@ProductIds,@SupplierIds) requested(product_id,supplier_id)
                  ON requested.product_id=sp.sku_id AND requested.supplier_id=sp.supplier_id
                WHERE (sp.is_deleted=FALSE OR sp.is_deleted IS NULL) ORDER BY sp.sku_id,sp.supplier_id,sp.uid
                """, parameters, Transaction));
        }
        // CLR Guid 比较顺序与提供者 UUID 排序不同，保留原方法最终客户端排序。
        return rows.OrderBy(item => item.SkuId).ThenBy(item => item.SupplierId).ThenBy(item => item.Uid).ToList();
    }
}
