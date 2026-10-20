using SwiftExport.Infrastructure.Repositories.DemandPlanning;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>采购管理行的贡献与来源摘要；完整关联历史仍由现有追溯API提供，不把合并需求伪装为单一来源行。</summary>
internal static class PurchaseOrderManagerAssociationSql
{
    /// <summary>按头保留指向缺失、软删或他单行的坏活动引用，不能因按行JOIN而漏成独立采购。</summary>
    internal const string OrphanHeadsCte="""
        manager_orphan_association_heads AS (
            SELECT DISTINCT link.target_head_id AS head_id FROM public.demand_execution_link link
            JOIN public.purchase_order_head h ON h.uid=link.target_head_id AND h.is_deleted=FALSE
            LEFT JOIN public.purchase_order_line target ON target.uid=link.target_line_id AND target.purchase_order_id=h.uid AND target.is_deleted=FALSE
            WHERE target.uid IS NULL AND (link.link_status=0 OR (link.is_deleted=FALSE
                AND (link.allocated_qty>link.released_qty OR link.allocated_qty<=0 OR link.released_qty<0 OR link.released_qty>link.allocated_qty)))
        )
        """;

    /// <summary>自带指定采购单的有效行范围，独立于收货权限核实贡献与守恒；正常作废释放只作历史展示。</summary>
    internal static string Ctes=>$"""
        {OrphanHeadsCte}, manager_association_scope AS (
            SELECT l.uid FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            WHERE h.uid=@PurchaseOrderId AND h.is_deleted=FALSE AND l.is_deleted=FALSE
        ), manager_association_evidence AS (
            SELECT l.uid AS line_id,link.net_demand_id,g.uid AS gross_id,g.source_uid,g.source_type,g.source_no,g.source_line_number,
                (link.link_status=0 OR link.allocated_qty>link.released_qty OR link.allocated_qty<=0 OR link.released_qty<0 OR link.released_qty>link.allocated_qty) AS is_current,
                CASE WHEN link.link_status=0 AND rtrim(link.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                     AND link.target_head_id=l.purchase_order_id AND link.allocated_qty>link.released_qty
                     THEN link.allocated_qty-link.released_qty ELSE 0 END AS linked_qty,
                CASE WHEN link.link_status IN(8,9) AND link.allocated_qty=link.released_qty AND link.allocated_qty>0 THEN FALSE
                    WHEN link.is_deleted IS DISTINCT FROM FALSE OR link.link_status IS DISTINCT FROM 0 OR rtrim(link.target_type,' ') COLLATE public.erp_dictionary_ci<>'PO'
                      OR link.target_head_id IS DISTINCT FROM l.purchase_order_id OR link.allocated_qty<=0 OR link.released_qty<0 OR link.released_qty>=link.allocated_qty
                      OR NOT COALESCE({MaterialDemandBasisSql.IsApplicable("n","g")},FALSE)
                      OR g.customer_id IS DISTINCT FROM h.customer_id OR l.product_id IS DISTINCT FROM n.product_id
                      OR rtrim(n.supply_type_snapshot,' ') COLLATE public.erp_dictionary_ci IS DISTINCT FROM '采购' COLLATE public.erp_dictionary_ci
                      OR (l.product_id IS NOT NULL AND (g.is_non_standard OR l.material_type COLLATE public.erp_dictionary_ci IS DISTINCT FROM n.material_type_snapshot COLLATE public.erp_dictionary_ci
                          OR l.supply_type COLLATE public.erp_dictionary_ci IS DISTINCT FROM n.supply_type_snapshot COLLATE public.erp_dictionary_ci))
                      OR (l.product_id IS NULL AND g.is_non_standard IS DISTINCT FROM TRUE)
                      OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(n.unit_snapshot,' '),'') IS NULL
                      OR btrim(l.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(n.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                      OR n.net_qty<=0 OR (SELECT COALESCE(SUM(other.allocated_qty-other.released_qty),0) FROM public.demand_execution_link other
                          WHERE other.net_demand_id=n.uid AND other.is_deleted=FALSE AND other.allocated_qty>other.released_qty
                            AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO')>n.net_qty
                    THEN TRUE ELSE FALSE END AS invalid
            FROM manager_association_scope scope JOIN public.purchase_order_line l ON l.uid=scope.uid
            JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            JOIN public.demand_execution_link link ON link.target_line_id=l.uid AND (link.is_deleted=FALSE OR link.link_status=0)
            LEFT JOIN public.net_demand n ON n.uid=link.net_demand_id LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id
        ), manager_association_totals AS (
            SELECT line_id,COALESCE(SUM(linked_qty) FILTER(WHERE is_current),0) AS linked_qty,
                COUNT(DISTINCT net_demand_id) FILTER(WHERE is_current)::integer AS linked_count,
                COUNT(DISTINCT net_demand_id)::integer AS historical_count,
                COUNT(*) FILTER(WHERE is_current)::integer AS current_count,COALESCE(bool_or(invalid),FALSE) AS invalid,
                CASE WHEN COUNT(DISTINCT source_uid)=1 AND COUNT(DISTINCT source_type)=1 THEN min(source_type) END AS source_type,
                CASE WHEN COUNT(DISTINCT source_uid)=1 AND COUNT(DISTINCT source_type)=1 THEN min(source_no) END AS source_no,
                CASE WHEN COUNT(DISTINCT net_demand_id)=1 THEN min(source_line_number) END AS source_line_number,
                concat(COUNT(DISTINCT net_demand_id),'项物料需求 / ',COUNT(DISTINCT gross_id),'项成品需求：',string_agg(DISTINCT source_no::text COLLATE "C",'，' ORDER BY source_no::text COLLATE "C")) AS source_summary
            FROM manager_association_evidence GROUP BY line_id
        ), manager_association_checked AS (
            SELECT l.uid AS line_id,t.linked_qty,COALESCE(t.linked_count,0) AS linked_count,COALESCE(t.historical_count,0) AS historical_count,
                t.source_type,t.source_no,t.source_line_number,t.source_summary,
                (h.lifecycle_status=9 AND COALESCE(t.current_count,0)=0 AND orphan.head_id IS NULL) AS historical_only,
                CASE WHEN orphan.head_id IS NOT NULL THEN '本单存在指向缺失或他单行的需求引用，需核实'
                    WHEN h.lifecycle_status=9 AND COALESCE(t.current_count,0)>0 THEN '已作废采购仍有活动或未释放贡献，需核实'
                    WHEN h.lifecycle_status=9 THEN NULL
                    WHEN COALESCE(t.invalid,FALSE) THEN '需求实物依据、客户、产品、单位、目标或承接容量需核实'
                    WHEN l.quantity<=0 OR l.unlinked_qty IS NULL OR l.unlinked_qty<0 OR l.unlinked_qty>l.quantity
                      OR COALESCE(t.linked_qty,0)+l.unlinked_qty<>l.quantity THEN '有效贡献与未关联数量不守恒，需核实'
                    ELSE NULL END AS review_reason
            FROM manager_association_scope scope JOIN public.purchase_order_line l ON l.uid=scope.uid
            JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id LEFT JOIN manager_association_totals t ON t.line_id=l.uid
            LEFT JOIN manager_orphan_association_heads orphan ON orphan.head_id=h.uid
        )
        """;
}
