using Dapper;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>物料编辑详情的专用读取，不改变普通列表及基础查询契约。</summary>
public partial class PostgreSqlProductRepository
{
    /// <inheritdoc />
    public async Task<ProductDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        // 可写资料包含主型号身份和物料自身型号，在 Uid 分隔前映射；来源名称、包装及审计在分隔后只供展示。
        var rows = await Connection.QueryAsync<ProductDetailWriteDto, ProductDetailDto, ProductDetailDto>(new CommandDefinition($"""
            SELECT p.sku AS "Sku",p.description_en AS "DescriptionEn",p.description_zh AS "DescriptionZh",
                p.features_en AS "FeaturesEn",p.features_zh AS "FeaturesZh",p.color_id AS "ColorId",
                p.material_type AS "MaterialType",p.unit AS "Unit",p.supply_type AS "SupplyType",
                p.note AS "Note",p.main_image AS "MainImage",
                p.product_master_id AS "ProductMasterId",p.link_sku AS "LinkSku",
                p.cny_price AS "CNYPrice",p.usd_price AS "USDPrice",p.unit_weight AS "UnitWeight",p.unit_cbm AS "UnitCBM",
                p.default_supplier_id AS "DefaultSupplierId",p.manufacturer_id AS "ManufacturerId",
                p.manufacturer_model AS "ManufacturerModel",p.default_warehouse_site_id AS "DefaultWarehouseSiteId",
                p.uid AS "Uid",p.version AS "Version",p.is_deleted AS "IsDeleted",p.is_active AS "IsActive",
                p.created_date AS "CreatedDate",p.created_by AS "CreatedBy",p.updated_date AS "UpdatedDate",p.updated_by AS "UpdatedBy",
                p.product_master_id AS "ProductMasterId",
                pm.master_sku AS "MasterSku",p.link_sku AS "LinkSku",
                {CatalogClassificationColumns},{CatalogReferenceColumns},pc.color_name_zh AS "Finish",
                pm.product_attribute_profile_id AS "ProductAttributeProfileId",
                COALESCE(NULLIF(profile.data_name_zh,''),profile.data_name_en) AS "ProductAttributeProfileName",
                p.bom_no AS "BomNo",p.bom_version AS "BomVersion",
                pkg.pcs_per_package AS "PcsOfCarton",pkg.nw_kg AS "NwOfCarton",pkg.gw_kg AS "GwOfCarton",pkg.cbm AS "CbmOfCarton"
            FROM public.product p {CatalogQueryJoins} {CatalogReferenceJoins}
            LEFT JOIN public.sys_dict_data profile ON profile.uid=pm.product_attribute_profile_id AND profile.is_deleted=FALSE
                AND profile.dict_code COLLATE public.erp_dictionary_ci=@ProfileCode COLLATE public.erp_dictionary_ci
            LEFT JOIN LATERAL (
                SELECT pp.pcs_per_package,pp.nw_kg,pp.gw_kg,pp.cbm FROM public.product_package pp
                WHERE pp.owner_product_id=p.uid AND (pp.is_deleted=FALSE OR pp.is_deleted IS NULL)
                ORDER BY pp.is_default DESC NULLS LAST,pp.pcs_per_package DESC NULLS LAST,pp.uid LIMIT 1
            ) pkg ON TRUE
            WHERE p.uid=@Uid AND p.is_deleted=FALSE
            """, new { Uid = uid, EmptyGuid = Guid.Empty, ProfileCode = DictCodes.ProductAttributeProfile }, Transaction,
                cancellationToken: cancellationToken), (data, detail) => { detail.Data = data; return detail; }, splitOn: "Uid");
        return rows.SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetIdsByProductMasterIdsAsync(IEnumerable<Guid> masterIds, CancellationToken cancellationToken = default)
    {
        var ids = masterIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await Connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT p.uid FROM public.product p
            WHERE p.product_master_id=ANY(@Ids) AND p.is_deleted=FALSE ORDER BY p.uid
            """, new { Ids = ids }, Transaction, cancellationToken: cancellationToken))).AsList();
    }
}
