using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text.Json;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Sources;

/// <summary>在当前 PostgreSQL 会话完整读取待建立来源和采购候选；保留原资格、快照及父项实际供应商事实，不接管事务。</summary>
public sealed class PostgreSqlMaterialDemandSourceRepository(RepositoryBaseContext context) : IMaterialDemandSourceRepository
{

    /// <summary>按来源类型与 Uid 聚合全部待建立行；历史或软删物料不阻挡重新建立，不按来源编号合并。</summary>
    public async Task<IReadOnlyList<SourceDemandCandidateGroupDto>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Session.Connection.QueryAsync<SourceDemandCandidateGroupDto>(Command($"""
            SELECT MIN(d.source_type COLLATE public.erp_dictionary_ci) AS "SourceType",
                   d.source_uid AS "SourceUid", MAX(d.source_no COLLATE public.erp_dictionary_ci) AS "SourceNo",
                   COUNT(*)::integer AS "CandidateCount"
            FROM "public"."gross_demand" d
            WHERE d.lifecycle_status = 0 AND d.is_deleted = FALSE
              AND NOT EXISTS (SELECT 1 FROM "public"."net_demand" nd
                  WHERE nd.gross_demand_id = d.uid AND nd.lifecycle_status = 0 AND nd.is_deleted = FALSE)
            GROUP BY rtrim(d.source_type, ' ') COLLATE public.erp_dictionary_ci, d.source_uid
            ORDER BY MAX(d.source_no COLLATE public.erp_dictionary_ci),
                     MIN(d.source_type COLLATE public.erp_dictionary_ci), d.source_uid;
            """, null, cancellationToken));
        return [.. rows];
    }

    /// <summary>按参数化来源类型与 Uid 完整返回待建立行；保持日期空值最后、来源行号空值最前及原快照，不分页。</summary>
    public async Task<IReadOnlyList<SourceDemandCandidateDto>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType, Guid sourceUid, CancellationToken cancellationToken = default)
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("SourceType", sourceType);
        parameters.Add("SourceUid", sourceUid);
        var rows = await context.Session.Connection.QueryAsync<SourceDemandCandidateDto>(Command($"""
            SELECT d.uid AS "GrossDemandId", d.version AS "SourceDemandVersion", d.source_version AS "SourceVersion",
                   d.source_type AS "SourceType", d.source_uid AS "SourceUid", d.source_no AS "SourceNo",
                   d.source_line_id AS "SourceLineId", d.source_line_number AS "SourceLineNumber",
                   d.sku_snapshot AS "Sku", d.link_sku_snapshot AS "LinkSku", d.customer_sku_snapshot AS "CustomerSku",
                   d.description_snapshot AS "Description", d.image_path_snapshot AS "ImagePath",
                   d.demand_qty AS "DemandQty", d.required_date AS "RequiredDate"
            FROM "public"."gross_demand" d
            WHERE rtrim(d.source_type, ' ') COLLATE public.erp_dictionary_ci
                    = rtrim(@SourceType, ' ') COLLATE public.erp_dictionary_ci AND d.source_uid = @SourceUid
              AND d.lifecycle_status = 0 AND d.is_deleted = FALSE
              AND NOT EXISTS (SELECT 1 FROM "public"."net_demand" nd
                  WHERE nd.gross_demand_id = d.uid AND nd.lifecycle_status = 0 AND nd.is_deleted = FALSE)
            ORDER BY d.required_date NULLS LAST, d.source_line_number NULLS FIRST, d.uid;
            """, parameters, cancellationToken));
        return [.. rows];
    }

    /// <summary>按原有效采购贡献和剩余量汇总来源候选；不增加工作台完整性筛选或 Allocation 条件。</summary>
    public async Task<IReadOnlyList<PurchaseDraftCandidateSourceGroupDto>> GetPurchaseCandidateSourceGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Session.Connection.QueryAsync<PurchaseDraftCandidateSourceGroupDto>(Command($"""
            SELECT MIN(gd.source_type COLLATE public.erp_dictionary_ci) AS "SourceType",
                   gd.source_uid AS "SourceUid", MAX(gd.source_no COLLATE public.erp_dictionary_ci) AS "SourceNo",
                   COUNT(*)::integer AS "CandidateCount"
            FROM "public"."net_demand" nd JOIN "public"."gross_demand" gd ON gd.uid = nd.gross_demand_id
            {PurchaseFactsSql}
            WHERE {EligibilitySql}
            GROUP BY rtrim(gd.source_type, ' ') COLLATE public.erp_dictionary_ci, gd.source_uid
            ORDER BY MIN(gd.source_type COLLATE public.erp_dictionary_ci),
                     MAX(gd.source_no COLLATE public.erp_dictionary_ci), gd.source_uid;
            """, PurchaseParameters(), cancellationToken));
        return [.. rows];
    }

    /// <summary>单次读取整来源或全部采购候选；仅使用当前物料图片、ASCII 单位修剪及严格父目标关系，解析私有 JSON 后返回公共事实。</summary>
    public async Task<IReadOnlyList<PurchaseDraftCandidateDto>> GetPurchaseCandidatesAsync(
        PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceFilter = request.SourceUid.HasValue ? """
            AND rtrim(gd.source_type, ' ') COLLATE public.erp_dictionary_ci
                = rtrim(@SourceType, ' ') COLLATE public.erp_dictionary_ci AND gd.source_uid = @SourceUid
            """ : string.Empty;
        var parameters = PurchaseParameters();
        // 无来源过滤时不绑定未定类型的 null 参数，保持一次命令且不替上层补做来源入参校验。
        if (request.SourceUid.HasValue)
        {
            parameters.Add("SourceType", request.SourceType);
            parameters.Add("SourceUid", request.SourceUid.Value);
        }
        var rows = await context.Session.Connection.QueryAsync<PurchaseDraftCandidateQueryRow>(Command($"""
            SELECT nd.uid AS "Uid", nd.version AS "Version", nd.lifecycle_status AS "LifecycleStatus",
                   nd.gross_demand_id AS "GrossDemandId", nd.decomposition_revision AS "DecompositionRevision",
                   nd.line_number AS "LineNumber", gd.customer_id AS "CustomerId",
                   customer.customer_code AS "CustomerCode", customer.customer_name AS "CustomerName",
                   gd.source_type AS "SourceType", gd.source_uid AS "SourceUid", gd.source_no AS "SourceNo",
                   gd.source_line_number AS "SourceLineNumber", gd.source_line_id AS "SourceLineId",
                   nd.root_net_demand_id AS "RootNetDemandId", root.sku_snapshot AS "RootSku",
                   nd.delivery_mode = 1 AS "IsCustomerSupplied", nd.delivery_target_net_demand_id AS "DeliveryTargetNetDemandId",
                   nd.product_id AS "ProductId", nd.product_id IS NULL AS "IsNonStandard",
                   nd.sku_snapshot AS "Sku", COALESCE(pn.product_code, '') AS "LinkSku",
                   nd.customer_sku_snapshot AS "CustomerSku", nd.description_snapshot AS "Description",
                   nd.color_snapshot AS "Color", nd.specification_snapshot AS "Specification",
                   COALESCE(p.main_image, '') AS "ImagePath", nd.material_type_snapshot AS "MaterialType",
                   NULLIF(btrim(nd.unit_snapshot, ' '), '') AS "Unit", nd.supply_type_snapshot AS "SupplyType",
                   nd.suggested_purchase_supplier_id AS "SuggestedPurchaseSupplierId",
                   suggestedSupplier.supplier_code AS "SuggestedPurchaseSupplierCode",
                   suggestedSupplier.supplier_name AS "SuggestedPurchaseSupplierName",
                   CASE WHEN nd.delivery_mode = 1 THEN deliveryTarget.suggested_purchase_supplier_id ELSE NULL::uuid END AS "SuggestedDeliverySupplierId",
                   CASE WHEN nd.delivery_mode = 1 THEN deliverySupplier.supplier_code ELSE NULL::text END AS "SuggestedDeliverySupplierCode",
                   CASE WHEN nd.delivery_mode = 1 THEN deliverySupplier.supplier_name ELSE NULL::text END AS "SuggestedDeliverySupplierName",
                   CASE WHEN nd.delivery_mode <> 1 THEN NULL::text
                        WHEN deliveryTarget.uid IS NULL THEN '送达目标关系无效'
                        WHEN deliveryTarget.suggested_purchase_supplier_id IS NULL THEN '父件尚未设置推荐采购供应商'
                        WHEN deliverySupplier.uid IS NULL THEN '父件推荐采购供应商已失效'
                        ELSE NULL::text END AS "SuggestedDeliverySupplierHint",
                   supplierProduct.supplier_sku AS "SuggestedSupplierSku", supplierProduct.unit_price AS "SuggestedUnitPrice",
                   supplierProduct.tax_price AS "SuggestedTaxPrice", nd.gross_qty AS "GrossQty", nd.net_qty AS "NetQty",
                   nd.required_date AS "DemandRequiredDate", COALESCE(purchaseFacts.associated_qty, 0) AS "AssociatedPurchaseQty",
                   purchaseNumbers.order_nos AS "PurchaseOrderNos",
                   COALESCE(parentSuppliers.facts, '[]'::jsonb)::text AS "ParentActualPurchaseSuppliersJson"
            FROM "public"."net_demand" nd JOIN "public"."gross_demand" gd ON gd.uid = nd.gross_demand_id
            LEFT JOIN "public"."customer" customer ON customer.uid = gd.customer_id AND COALESCE(customer.is_deleted, FALSE) = FALSE
            LEFT JOIN "public"."net_demand" root ON root.uid = nd.root_net_demand_id
            LEFT JOIN "public"."product" p ON p.uid = nd.product_id AND COALESCE(p.is_deleted, FALSE) = FALSE
            LEFT JOIN public.product_neutral pn ON pn.uid=p.product_neutral_id AND pn.is_deleted=FALSE
            LEFT JOIN "public"."supplier" suggestedSupplier ON suggestedSupplier.uid = nd.suggested_purchase_supplier_id
                AND COALESCE(suggestedSupplier.is_deleted, FALSE) = FALSE
            LEFT JOIN "public"."supplier_product" supplierProduct ON supplierProduct.supplier_id = nd.suggested_purchase_supplier_id
                AND supplierProduct.sku_id = nd.product_id AND COALESCE(supplierProduct.is_deleted, FALSE) = FALSE
            LEFT JOIN "public"."net_demand" deliveryTarget ON deliveryTarget.uid = nd.delivery_target_net_demand_id
                AND deliveryTarget.uid = nd.parent_net_demand_id AND deliveryTarget.gross_demand_id = nd.gross_demand_id
                AND deliveryTarget.root_net_demand_id = nd.root_net_demand_id
                AND deliveryTarget.decomposition_revision = nd.decomposition_revision
                AND deliveryTarget.lifecycle_status = 0 AND deliveryTarget.is_deleted = FALSE
            LEFT JOIN "public"."supplier" deliverySupplier ON deliverySupplier.uid = deliveryTarget.suggested_purchase_supplier_id
                AND COALESCE(deliverySupplier.is_deleted, FALSE) = FALSE
            {PurchaseFactsSql}
            LEFT JOIN LATERAL (
                SELECT string_agg(numbers.order_no, ',') AS order_nos FROM (
                    SELECT DISTINCT ON (rtrim(purchaseHead.order_no, ' ') COLLATE public.erp_dictionary_ci) purchaseHead.order_no
                    FROM "public"."demand_execution_link" link
                    JOIN "public"."purchase_order_head" purchaseHead ON purchaseHead.uid = link.target_head_id
                        AND purchaseHead.lifecycle_status = 0 AND COALESCE(purchaseHead.is_deleted, FALSE) = FALSE
                    JOIN "public"."purchase_order_line" purchaseLine ON purchaseLine.uid = link.target_line_id
                        AND purchaseLine.purchase_order_id = purchaseHead.uid AND COALESCE(purchaseLine.is_deleted, FALSE) = FALSE
                    WHERE link.net_demand_id = nd.uid AND {TargetMatch("link")}
                        AND link.link_status = 0 AND COALESCE(link.is_deleted, FALSE) = FALSE
                ) numbers
            ) purchaseNumbers ON TRUE
            LEFT JOIN LATERAL (
                SELECT jsonb_agg(jsonb_strip_nulls(jsonb_build_object(
                    'SupplierId', grouped.supplier_id, 'SupplierCode', grouped.supplier_code,
                    'SupplierName', grouped.supplier_name, 'IsActive', grouped.is_active,
                    'PurchaseOrderNos', grouped.order_nos))
                    ORDER BY grouped.supplier_code COLLATE public.erp_dictionary_ci NULLS FIRST, grouped.supplier_id) AS facts
                FROM (
                    SELECT facts.supplier_id, MAX(facts.supplier_code COLLATE public.erp_dictionary_ci) AS supplier_code,
                           MAX(facts.supplier_name COLLATE public.erp_dictionary_ci) AS supplier_name,
                           bool_or(activeSupplier.uid IS NOT NULL) AS is_active, string_agg(facts.order_no, ',') AS order_nos
                    FROM (
                        SELECT DISTINCT ON (purchaseHead.supplier_id,
                            rtrim(purchaseHead.supplier_code, ' ') COLLATE public.erp_dictionary_ci,
                            rtrim(purchaseHead.supplier_name, ' ') COLLATE public.erp_dictionary_ci,
                            rtrim(purchaseHead.order_no, ' ') COLLATE public.erp_dictionary_ci)
                            purchaseHead.supplier_id, purchaseHead.supplier_code, purchaseHead.supplier_name, purchaseHead.order_no
                        FROM "public"."demand_execution_link" parentLink
                        JOIN "public"."purchase_order_head" purchaseHead ON purchaseHead.uid = parentLink.target_head_id
                            AND purchaseHead.lifecycle_status = 0 AND COALESCE(purchaseHead.is_deleted, FALSE) = FALSE
                        JOIN "public"."purchase_order_line" purchaseLine ON purchaseLine.uid = parentLink.target_line_id
                            AND purchaseLine.purchase_order_id = purchaseHead.uid AND COALESCE(purchaseLine.is_deleted, FALSE) = FALSE
                        WHERE parentLink.net_demand_id = deliveryTarget.uid AND {TargetMatch("parentLink")}
                            AND parentLink.link_status = 0 AND COALESCE(parentLink.is_deleted, FALSE) = FALSE
                    ) facts
                    LEFT JOIN "public"."supplier" activeSupplier ON activeSupplier.uid = facts.supplier_id
                        AND activeSupplier.is_active = TRUE AND COALESCE(activeSupplier.is_deleted, FALSE) = FALSE
                    GROUP BY facts.supplier_id
                ) grouped
            ) parentSuppliers ON TRUE
            WHERE {EligibilitySql}
            {sourceFilter}
            ORDER BY gd.source_type COLLATE public.erp_dictionary_ci, gd.source_no COLLATE public.erp_dictionary_ci,
                     gd.source_uid, gd.source_line_number NULLS FIRST, nd.line_number, nd.uid;
            """, parameters, cancellationToken));
        var result = rows.ToList();
        foreach (var row in result)
            row.ParentActualPurchaseSuppliers = JsonSerializer.Deserialize<List<PurchaseDraftActualSupplierFactDto>>(
                row.ParentActualPurchaseSuppliersJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return result;
    }

    /// <summary>只计算原候选认可的有效目标采购贡献；不增加正剩余限制、客户或单位资格。</summary>
    private static string PurchaseFactsSql => $"""
        LEFT JOIN LATERAL (
            SELECT SUM(link.allocated_qty - link.released_qty) AS associated_qty FROM "public"."demand_execution_link" link
            JOIN "public"."purchase_order_head" purchaseHead ON purchaseHead.uid = link.target_head_id
                AND purchaseHead.lifecycle_status = 0 AND COALESCE(purchaseHead.is_deleted, FALSE) = FALSE
            JOIN "public"."purchase_order_line" purchaseLine ON purchaseLine.uid = link.target_line_id
                AND purchaseLine.purchase_order_id = purchaseHead.uid AND COALESCE(purchaseLine.is_deleted, FALSE) = FALSE
            WHERE link.net_demand_id = nd.uid AND {TargetMatch("link")}
                AND link.link_status = 0 AND COALESCE(link.is_deleted, FALSE) = FALSE
        ) purchaseFacts ON TRUE
        """;

    private const string EligibilitySql = """
        nd.lifecycle_status = 0 AND COALESCE(nd.is_deleted, FALSE) = FALSE
        AND gd.lifecycle_status = 0 AND COALESCE(gd.is_deleted, FALSE) = FALSE
        AND rtrim(nd.supply_type_snapshot, ' ') COLLATE public.erp_dictionary_ci
            = rtrim(@PurchaseSupplyType, ' ') COLLATE public.erp_dictionary_ci
        AND nd.net_qty > COALESCE(purchaseFacts.associated_qty, 0)
        """;

    /// <summary>保持目标类型尾 ASCII 空格与冻结 ICU 比较，不改写存储快照或扩大 Unicode 等价声明。</summary>
    private static string TargetMatch(string alias) => $"rtrim({alias}.target_type, ' ') COLLATE public.erp_dictionary_ci = rtrim(@PurchaseTargetType, ' ') COLLATE public.erp_dictionary_ci";

    /// <summary>绑定候选固定值；不接管业务入参归一化。</summary>
    private static PostgreSqlParameters PurchaseParameters()
    {
        var parameters = new PostgreSqlParameters();
        parameters.Add("PurchaseTargetType", "PO");
        parameters.Add("PurchaseSupplyType", SupplyTypeEnums.采购);
        return parameters;
    }

    /// <summary>将单条只读查询交给当前会话，允许无事务并原样传递已有事务和取消。</summary>
    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);


    /// <summary>接收私有 JSON 列；解析后公共契约仅暴露结构化供应商事实。</summary>
    private sealed class PurchaseDraftCandidateQueryRow : PurchaseDraftCandidateDto
    {
        public string ParentActualPurchaseSuppliersJson { get; set; } = "[]";
    }
}
