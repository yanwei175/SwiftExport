namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Purchases;
/// <summary>关联明细和当前承接汇总共用同一目标、单位和修订核实条件。</summary>
internal static class OrderTrackingPurchaseSql
{
    internal static string Review=>$"""
        CASE WHEN l.is_deleted OR l.link_status<>0 THEN '历史采购关联'
                    WHEN l.allocated_qty<=0 OR l.released_qty<0 OR l.released_qty>l.allocated_qty THEN '采购贡献数量需核实'
                    WHEN pol.uid IS NULL OR poh.uid IS NULL THEN '采购目标缺失或已删除'
                    WHEN poh.lifecycle_status=9 THEN '采购已作废'
                    WHEN poh.customer_id IS NULL OR g.customer_id IS NULL OR poh.customer_id<>g.customer_id THEN '客户归属需核实'
                    WHEN pol.product_id IS DISTINCT FROM n.product_id THEN '采购与物料产品不一致'
                    WHEN NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(n.unit_snapshot,' '),'') IS NULL
                      OR btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(n.unit_snapshot,' ') COLLATE public.erp_dictionary_ci THEN '采购与物料单位需核实'
                    ELSE {OrderTrackingEvidenceSql.MaterialReview} END
        """;
    internal const string From="""
        FROM public.net_demand n LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id
            JOIN public.demand_execution_link l ON l.net_demand_id=n.uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
            LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
            LEFT JOIN public.supplier supplier ON supplier.uid=poh.supplier_id AND supplier.is_deleted=FALSE
        """;
}
