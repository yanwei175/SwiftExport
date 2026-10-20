using SwiftExport.Infrastructure.Repositories.DemandPlanning;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>按真实采购头确认状态分类需求占用；异常引用必须保留并阻断，不能漏算为空余容量。</summary>
internal static class PurchaseDemandCommitmentSql
{
    /// <summary>活动关系及未正常释放的异常历史均参与核实；正常取消且全量释放的历史不占用。</summary>
    internal const string Relevant = "(link.link_status=0 OR link.allocated_qty<>link.released_qty OR link.allocated_qty<=0 OR link.released_qty<0 OR link.link_status NOT IN(0,8,9))";

    /// <summary>读取一个物料的全部采购占用证据；不按目标状态过滤，关闭的已确认采购仍保存承诺。</summary>
    internal static string Evidence(string demandId) => $"""
        SELECT link.uid,link.net_demand_id,link.target_head_id,link.target_line_id,
            link.allocated_qty-link.released_qty AS qty,head.confirmation_status,head.order_no,
            CASE WHEN link.is_deleted OR link.link_status<>0 OR link.allocated_qty<=0 OR link.released_qty<0
                    OR link.released_qty>=link.allocated_qty THEN '采购贡献状态或数量需核实'
                WHEN head.uid IS NULL OR head.is_deleted OR line.uid IS NULL OR line.is_deleted
                    OR head.lifecycle_status NOT IN(0,8) OR head.confirmation_status NOT IN(0,1)
                    OR (head.lifecycle_status=8 AND head.confirmation_status<>1) THEN '采购目标或确认状态需核实'
                WHEN NOT COALESCE({MaterialDemandBasisSql.IsApplicable("material", "source")},FALSE)
                    THEN '成品需求实物依据需核实'
                WHEN head.customer_id IS NULL OR head.customer_id IS DISTINCT FROM source.customer_id
                    OR line.product_id IS DISTINCT FROM material.product_id THEN '采购客户或产品需核实'
                WHEN rtrim(material.supply_type_snapshot,' ') COLLATE public.erp_dictionary_ci<>'采购'
                    OR (material.product_id IS NULL AND NOT source.is_non_standard)
                    OR (material.product_id IS NOT NULL AND source.is_non_standard)
                    OR (material.product_id IS NOT NULL AND (line.material_type IS DISTINCT FROM material.material_type_snapshot
                        OR line.supply_type IS DISTINCT FROM material.supply_type_snapshot)) THEN '采购物料角色或供货方式需核实'
                WHEN NULLIF(btrim(material.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(line.unit_snapshot,' '),'') IS NULL
                    OR btrim(material.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(line.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                    THEN '采购单位需核实'
                WHEN line.quantity<=0 OR line.unlinked_qty IS NULL OR line.unlinked_qty<0 OR line.unlinked_qty>line.quantity
                    OR line.unlinked_qty+(SELECT COALESCE(sum(shared.allocated_qty-shared.released_qty),0)
                        FROM public.demand_execution_link shared WHERE shared.target_head_id=head.uid AND shared.target_line_id=line.uid
                            AND rtrim(shared.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                            AND shared.link_status=0 AND NOT shared.is_deleted)<>line.quantity THEN '采购行数量守恒需核实'
                ELSE '' END AS review_reason
        FROM public.demand_execution_link link
        LEFT JOIN public.purchase_order_head head ON head.uid=link.target_head_id
        LEFT JOIN public.purchase_order_line line ON line.uid=link.target_line_id AND line.purchase_order_id=link.target_head_id
        LEFT JOIN public.net_demand material ON material.uid=link.net_demand_id
        LEFT JOIN public.gross_demand source ON source.uid=material.gross_demand_id
        WHERE link.net_demand_id={demandId} AND rtrim(link.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND {Relevant}
        """;

    /// <summary>返回总占用、已采购和草稿占用；调用方有核实原因时对外返回未知数量。</summary>
    internal static string Aggregate(string demandId) => $"""
        WITH evidence AS ({Evidence(demandId)})
        SELECT COALESCE(sum(qty),0) AS associated_qty,
            COALESCE(sum(qty) FILTER(WHERE confirmation_status=1),0) AS purchased_qty,
            COALESCE(sum(qty) FILTER(WHERE confirmation_status=0),0) AS draft_qty,
            COALESCE(string_agg(DISTINCT NULLIF(review_reason,''),'；'),'') AS review_reason,
            COALESCE((SELECT jsonb_agg(jsonb_build_object('PurchaseOrderId',orders.target_head_id,'OrderNo',orders.order_no)
                ORDER BY orders.order_no,orders.target_head_id) FROM (
                SELECT DISTINCT target_head_id,order_no FROM evidence WHERE confirmation_status=0 AND review_reason='') orders),'[]'::jsonb)::text AS draft_orders_json
        FROM evidence
        """;
}
