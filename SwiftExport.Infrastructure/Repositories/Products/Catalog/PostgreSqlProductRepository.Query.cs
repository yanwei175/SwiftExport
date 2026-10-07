using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>产品普通读取保留各入口软删范围及当前关联展示，不额外取得更新锁。</summary>
public partial class PostgreSqlProductRepository
{
    /// <summary>从真实客户产品关系取得当前非空客户代码；不读取关系快照或产品旧列，不排除停用记录。</summary>
    public async Task<IReadOnlyList<string>> GetAllCustomerCodeListAsync()
    {
        const string sql = """
            SELECT DISTINCT c.customer_code COLLATE public.erp_dictionary_ci AS code
            FROM public.customer c
            WHERE c.is_deleted=FALSE AND rtrim(c.customer_code,' ')<>''
              AND EXISTS (
                SELECT 1 FROM public.customer_product cp
                INNER JOIN public.product p ON p.uid=cp.sku_id AND p.is_deleted=FALSE
                WHERE cp.customer_id=c.uid AND cp.is_deleted=FALSE)
            ORDER BY code
            """;
        return (await Connection.QueryAsync<string>(sql, transaction: Transaction)).AsList();
    }

    private const string CatalogEntityColumns = """
        p."uid" AS "Uid",
        p."version" AS "Version",
        p."is_deleted" AS "IsDeleted",
        p."created_date" AS "CreatedDate",
        p."created_by" AS "CreatedBy",
        p."updated_date" AS "UpdatedDate",
        p."updated_by" AS "UpdatedBy",
        p."is_active" AS "IsActive",
        p."product_master_id" AS "ProductMasterId",
        p."master_sku" AS "MasterSku",
        p."sku" AS "Sku",
        p."description_en" AS "DescriptionEn",
        p."description_zh" AS "DescriptionZh",
        p."color_id" AS "ColorId",
        p."features_en" AS "FeaturesEn",
        p."features_zh" AS "FeaturesZh",
        p."material_type" AS "MaterialType",
        p."unit" AS "Unit",
        p."supply_type" AS "SupplyType",
        p."bom_version" AS "BomVersion",
        p."bom_no" AS "BomNo",
        p."note" AS "Note",
        p."link_sku" AS "LinkSku",
        p."main_image" AS "MainImage",
        p."cny_price" AS "CNYPrice",
        p."usd_price" AS "USDPrice",
        p."unit_cbm" AS "UnitCBM",
        p."unit_weight" AS "UnitWeight",
        p."default_supplier_id" AS "DefaultSupplierId",
        p."default_warehouse_site_id" AS "DefaultWarehouseSiteId",
        p."manufacturer_id" AS "ManufacturerId",
        p."manufacturer_model" AS "ManufacturerModel"
    """;
    private const string CatalogDtoColumns = """
        p."uid" AS "Uid",
        p."version" AS "Version",
        p."is_deleted" AS "IsDeleted",
        p."created_date" AS "CreatedDate",
        p."created_by" AS "CreatedBy",
        p."updated_date" AS "UpdatedDate",
        p."updated_by" AS "UpdatedBy",
        p."master_sku" AS "MasterSku",
        p."sku" AS "Sku",
        p."description_en" AS "DescriptionEn",
        p."description_zh" AS "DescriptionZh",
        p."features_en" AS "FeaturesEn",
        p."features_zh" AS "FeaturesZh",
        p."material_type" AS "MaterialType",
        p."unit" AS "Unit",
        p."supply_type" AS "SupplyType",
        p."bom_version" AS "BomVersion",
        p."bom_no" AS "BomNo",
        p."note" AS "Note",
        p."link_sku" AS "LinkSku",
        p."main_image" AS "MainImage",
        p."cny_price" AS "CNYPrice",
        p."usd_price" AS "USDPrice",
        p."color_id" AS "ColorId",
        p."default_supplier_id" AS "DefaultSupplierId",
        p."default_warehouse_site_id" AS "DefaultWarehouseSiteId",
        p."manufacturer_id" AS "ManufacturerId",
        p."manufacturer_model" AS "ManufacturerModel"
    """;
    private const string CatalogClassificationColumns = """
        COALESCE(pm.category_id,@EmptyGuid) AS "CategoryId",
        CASE WHEN pm.uid IS NULL OR pm.category_id IS NULL OR pm.category_id=@EmptyGuid THEN '未分类'
             ELSE COALESCE(pat.category_name,'分类已失效') END AS "CategoryName"
        """;
    private const string CatalogReferenceColumns = """
        defaultSupplier.supplier_code AS "DefaultSupplierCode",defaultSupplier.supplier_name AS "SupplierName",
        manufacturer.supplier_code AS "ManufacturerCode",manufacturer.supplier_name AS "ManufacturerName",
        warehouse.code AS "DefaultWarehouseCode",warehouse.name AS "DefaultWarehouseName"
        """;
    private const string CatalogReferenceJoins = """
        LEFT JOIN public.supplier manufacturer ON manufacturer.uid=p.manufacturer_id AND manufacturer.is_deleted=FALSE
        LEFT JOIN public.warehouse_site warehouse ON warehouse.uid=p.default_warehouse_site_id AND warehouse.is_deleted=FALSE
        """;
    private const string CatalogQueryJoins = """
        LEFT JOIN public.product_master pm ON pm.uid=p.product_master_id AND (pm.is_deleted=FALSE OR pm.is_deleted IS NULL)
        LEFT JOIN public.product_category pat ON pat.uid=pm.category_id AND (pat.is_deleted=FALSE OR pat.is_deleted IS NULL)
        LEFT JOIN public.product_color pc ON pc.uid=p.color_id AND (pc.is_deleted=FALSE OR pc.is_deleted IS NULL)
        LEFT JOIN public.supplier defaultSupplier ON defaultSupplier.uid=p.default_supplier_id AND (defaultSupplier.is_deleted=FALSE OR defaultSupplier.is_deleted IS NULL)
        """;

    /// <summary>按原始 SKU 查未删除产品，尾部 ASCII 空格及大小写比较兼容原等值规则，停用产品仍可读取。</summary>
    public Task<Product?> GetBySkuAsync(string sku) => Connection.QueryFirstOrDefaultAsync<Product>(
        $"SELECT {CatalogEntityColumns} FROM public.product p WHERE {CatalogEqual("p.sku", "@Sku")} AND p.is_deleted=FALSE ORDER BY p.uid LIMIT 1",
        new { Sku = sku }, Transaction);

    /// <summary>详情只投影现有 DTO 字段；已失效供应商仓库隐藏名称，保留产品上的逻辑标识。</summary>
    public Task<ProductDto?> GetDtoByIdAsync(Guid uid) => Connection.QuerySingleOrDefaultAsync<ProductDto>($"""
        SELECT {CatalogDtoColumns},p.is_active AS "IsActive",{CatalogReferenceColumns}
        FROM public.product p {CatalogReferenceJoins}
        LEFT JOIN public.supplier defaultSupplier ON defaultSupplier.uid=p.default_supplier_id AND defaultSupplier.is_deleted=FALSE
        WHERE p.uid=@Uid AND (p.is_deleted=FALSE OR p.is_deleted IS NULL)
        """, new { Uid = uid }, Transaction);

    /// <summary>按原始 SKU 读取 DTO，分类从当前主型号取得，空价格保持 DTO 原有默认映射。</summary>
    public Task<ProductDto?> GetDtoBySkuAsync(string sku) => Connection.QueryFirstOrDefaultAsync<ProductDto>($"""
        SELECT {CatalogDtoColumns},{CatalogClassificationColumns},{CatalogReferenceColumns},pc.color_name_zh AS "Finish"
        FROM public.product p {CatalogQueryJoins} {CatalogReferenceJoins}
        WHERE {CatalogEqual("p.sku", "@Sku")} AND (p.is_deleted=FALSE OR p.is_deleted IS NULL)
        ORDER BY p.uid LIMIT 1
        """, new { Sku = sku, EmptyGuid = Guid.Empty }, Transaction);

    /// <summary>空 BOM 编号直接返回；非空输入 Trim 后只读取严格未删除产品。</summary>
    public Task<Product?> GetByBomNoAsync(string bomNo)
    {
        if (string.IsNullOrWhiteSpace(bomNo)) return Task.FromResult<Product?>(null);
        return Connection.QueryFirstOrDefaultAsync<Product>(
            $"SELECT {CatalogEntityColumns} FROM public.product p WHERE {CatalogEqual("p.bom_no", "@BomNo")} AND p.is_deleted=FALSE ORDER BY p.uid LIMIT 1",
            new { BomNo = bomNo.Trim() }, Transaction);
    }

    /// <summary>SKU 集合按原入口 Trim、忽略大小写去重；空集合不访问数据库，软删为空仍可读取。</summary>
    public async Task<IReadOnlyList<Product>> GetBySkuListAsync(IEnumerable<string> skuList)
    {
        var values = skuList?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (values.Length == 0) return [];
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("SkuList", values);
        return [.. await Connection.QueryAsync<Product>($"""
            SELECT {CatalogEntityColumns} FROM public.product p
            WHERE (p.is_deleted=FALSE OR p.is_deleted IS NULL) AND {CatalogTextSet("p.sku", "SkuList")}
            """, parameters, Transaction)];
    }

    /// <summary>现存 SKU 查询沿用原未规范化输入和包含软删行的范围，不替代普通有效产品查询。</summary>
    public async Task<IReadOnlyList<string>> GetExistingSkuListAsync(IEnumerable<string> skuList)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("SkuList", skuList);
        return [.. await Connection.QueryAsync<string>(
            $"SELECT p.sku AS \"Sku\" FROM public.product p WHERE {CatalogTextSet("p.sku", "SkuList")}", parameters, Transaction)];
    }

    /// <summary>等值比较只修剪尾部 ASCII 空格；显式 ICU 不修剪前导空格或更改输入。</summary>
    private static string CatalogEqual(string column, string value) =>
        $"rtrim({column},' ') COLLATE public.erp_dictionary_ci = rtrim({value},' ') COLLATE public.erp_dictionary_ci";

    /// <summary>文本数组使用 EXISTS 避免重复输入扩大行数，沿用同一尾空格和大小写等值语义。</summary>
    private static string CatalogTextSet(string column, string parameter) =>
        $"EXISTS (SELECT 1 FROM unnest(@{parameter}) input(code) WHERE {CatalogEqual(column, "input.code")})";
}
