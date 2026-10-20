using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Purchases;
/// <summary>关联明细和当前承接汇总共用同一目标、单位和修订核实条件。</summary>
internal static class OrderTrackingPurchaseSql
{
    /// <summary>异常活动及未释放历史都须核实，正常全释放历史仅展示。</summary>
    internal const string Current="(l.link_status=0 OR l.allocated_qty<>l.released_qty OR l.allocated_qty<=0 OR l.released_qty<0 OR l.link_status NOT IN(0,8,9))";
    internal static string Review=>$"""
        CASE WHEN NOT {Current} THEN '历史采购关联'
            ELSE COALESCE((SELECT evidence.review_reason FROM ({PurchaseDemandCommitmentSql.Evidence("n.uid")}) evidence
                WHERE evidence.uid=l.uid),'采购贡献需核实') END
        """;
    internal const string From="""
        FROM public.net_demand n LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id
            JOIN public.demand_execution_link l ON l.net_demand_id=n.uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
            LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
            LEFT JOIN public.supplier supplier ON supplier.uid=poh.supplier_id AND supplier.is_deleted=FALSE
        """;
}
