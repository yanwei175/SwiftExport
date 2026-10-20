using SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

using SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Purchases;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Summary;

/// <summary>摘要与关注项筛选共用批量事实；范围仅由仓储内的固定谓词构造。</summary>
internal static class OrderTrackingSummarySql
{
    // 来源一致性仅在基础摘要或来源核实筛选时计算，避免其他维度重复关联完整来源。
    private static string SourceReview => $"""
CASE WHEN g.source_uid IS NULL OR g.source_uid='00000000-0000-0000-0000-000000000000'::uuid
              OR g.source_line_id IS NULL OR g.source_line_id='00000000-0000-0000-0000-000000000000'::uuid
              OR (g.source_type COLLATE "C"='MD' AND g.source_version IS NOT NULL)
              OR (g.source_type COLLATE "C"<>'MD' AND (g.source_version IS NULL OR g.source_version<=0))
              OR g.demand_basis_fingerprint IS NULL OR char_length(g.demand_basis_fingerprint)<>64
              OR g.demand_basis_fingerprint COLLATE "C" ~ '[^0-9A-F]'
              OR g.customer_id IS NULL OR c.uid IS NULL OR NULLIF(btrim(g.source_no,' '),'') IS NULL
              OR NULLIF(btrim(g.unit_snapshot,' '),'') IS NULL OR g.demand_qty<=0
              OR EXISTS(SELECT 1 FROM public.gross_demand peer WHERE NOT peer.is_deleted AND peer.uid<>g.uid
                AND upper(rtrim(peer.source_type,' ')) COLLATE public.erp_dictionary_ci={OrderTrackingFilters.GroupType}
                AND COALESCE(NULLIF(peer.source_uid,'00000000-0000-0000-0000-000000000000'::uuid),peer.uid)={OrderTrackingFilters.GroupId}
                AND (peer.customer_id IS DISTINCT FROM g.customer_id OR peer.source_no IS DISTINCT FROM g.source_no))
              THEN TRUE ELSE FALSE END
""";
    internal static string Base(string scope,bool includeSourceReview=false) => $"""
        summary_base AS (
          SELECT g.uid AS gross_id,g.version,g.unit_snapshot,g.demand_qty,g.required_date,g.lifecycle_status,
            {OrderTrackingFilters.GroupType} AS source_type,{OrderTrackingFilters.GroupId} AS group_id,
            {(includeSourceReview?SourceReview:"FALSE")} AS source_review
          {OrderTrackingFactsSql.From} WHERE {OrderTrackingFactsSql.Eligible} AND ({scope})
        )
        """;

    internal static string Delivery => OrderTrackingDeliverySql.Ctes("g.uid IN(SELECT gross_id FROM summary_base)");
    internal static string Material => $"""
        summary_material AS (
          SELECT n.uid,n.gross_demand_id AS gross_id,n.net_qty,n.supply_type_snapshot,n.material_type_snapshot,
            (NOT n.is_deleted AND n.lifecycle_status=0) AS active,
            {OrderTrackingEvidenceSql.MaterialReview} AS review_reason
          FROM public.net_demand n JOIN public.gross_demand g ON g.uid=n.gross_demand_id
          WHERE n.gross_demand_id IN(SELECT gross_id FROM summary_base)
        ), summary_material_counts AS (
          SELECT b.gross_id,count(m.uid) FILTER(WHERE m.active)::int AS active_count,
            count(m.uid) FILTER(WHERE NOT m.active)::int AS history_count,
            count(m.uid) FILTER(WHERE m.active AND m.review_reason<>'')::int AS review_count
          FROM summary_base b LEFT JOIN summary_material m ON m.gross_id=b.gross_id GROUP BY b.gross_id
        )
        """;

    // 与采购草稿资格一致：采购型快照才参与承接；客供和其他执行方式不算缺采购。
    internal static string Purchase => $"""
        summary_purchase_links AS (
          SELECT n.uid AS net_id,l.allocated_qty-l.released_qty AS qty,poh.confirmation_status,{OrderTrackingPurchaseSql.Review} AS review_reason
          {OrderTrackingPurchaseSql.From}
          WHERE n.uid IN(SELECT uid FROM summary_material WHERE active)
            AND {OrderTrackingPurchaseSql.Current}
        ), summary_purchase_material AS (
          SELECT m.gross_id,m.uid,m.net_qty,
            COALESCE((SELECT sum(p.qty) FROM summary_purchase_links p WHERE p.net_id=m.uid),0) AS occupied_qty,
            CASE WHEN m.review_reason='' AND NOT EXISTS(SELECT 1 FROM summary_purchase_links p WHERE p.net_id=m.uid AND p.review_reason<>'')
                AND COALESCE((SELECT sum(p.qty) FROM summary_purchase_links p WHERE p.net_id=m.uid),0)<=m.net_qty
              THEN COALESCE((SELECT sum(p.qty) FROM summary_purchase_links p WHERE p.net_id=m.uid AND p.confirmation_status=1),0) END AS associated_qty
          FROM summary_material m WHERE m.active
            AND rtrim(m.supply_type_snapshot,' ') COLLATE public.erp_dictionary_ci='采购'
            AND COALESCE(rtrim(m.material_type_snapshot,' '),'') COLLATE public.erp_dictionary_ci<>'客供件'
        ), summary_purchase_counts AS (
          SELECT b.gross_id,count(m.uid)::int AS applicable_count,
            count(m.uid) FILTER(WHERE associated_qty<net_qty)::int AS shortfall_count,
            count(m.uid) FILTER(WHERE occupied_qty>net_qty)::int AS excess_count,
            count(m.uid) FILTER(WHERE associated_qty IS NULL)::int AS review_count,
            count(m.uid) FILTER(WHERE associated_qty>0)::int AS allocated_count
          FROM summary_base b LEFT JOIN summary_purchase_material m ON m.gross_id=b.gross_id GROUP BY b.gross_id
        )
        """;

    internal static string Receipts => $"""
        summary_related AS (
          SELECT DISTINCT n.gross_demand_id AS gross_id,l.target_line_id,l.target_head_id
          FROM public.net_demand n JOIN public.gross_demand g ON g.uid=n.gross_demand_id
          JOIN public.demand_execution_link l ON l.net_demand_id=n.uid
          LEFT JOIN public.purchase_order_head relatedHead ON relatedHead.uid=l.target_head_id
          WHERE n.gross_demand_id IN(SELECT gross_id FROM summary_base WHERE lifecycle_status=0)
            AND {OrderTrackingReceiptScopeSql.Include("relatedHead","l.target_line_id")}
            AND NOT n.is_deleted AND n.lifecycle_status=0
            AND NOT l.is_deleted AND l.link_status=0
            AND (l.allocated_qty<>l.released_qty OR l.allocated_qty<=0 OR l.released_qty<0)
            AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
        ),
        {PurchaseReceiptFactsSql.Ctes("SELECT DISTINCT target_line_id AS uid FROM summary_related")}, summary_receipt_targets AS (
          SELECT target_line_id,min(target_head_id::text)::uuid AS head_id,count(DISTINCT target_head_id) AS head_count
          FROM summary_related GROUP BY target_line_id
        ), summary_receipt_lines AS (
          SELECT t.target_line_id AS line_id,
            CASE WHEN t.head_count=1 AND pol.uid IS NOT NULL AND poh.uid IS NOT NULL AND NOT pol.is_deleted AND NOT poh.is_deleted
              AND poh.confirmation_status=1 AND pol.quantity>0 AND NULLIF(btrim(pol.unit_snapshot,' '),'') IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM receipt_evidence e WHERE e.source_line_id=t.target_line_id AND e.invalid)
              THEN pol.quantity-COALESCE((SELECT sum(e.quantity) FROM receipt_evidence e
                  WHERE e.source_line_id=t.target_line_id AND e.posting_status=1 AND NOT e.invalid),0) END AS remaining_qty,
            EXISTS(SELECT 1 FROM public.demand_execution_link other WHERE other.target_line_id=t.target_line_id
              AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
              AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty
              GROUP BY other.target_line_id HAVING count(DISTINCT other.net_demand_id)>1) AS shared
          FROM summary_receipt_targets t LEFT JOIN public.purchase_order_line pol ON pol.uid=t.target_line_id AND pol.purchase_order_id=t.head_id
          LEFT JOIN public.purchase_order_head poh ON poh.uid=t.head_id
        ), summary_receipt_scope AS (
          SELECT DISTINCT r.gross_id,l.line_id,l.remaining_qty,l.shared FROM summary_related r JOIN summary_receipt_lines l ON l.line_id=r.target_line_id
        ), summary_receipt_counts AS (
          SELECT b.gross_id,count(l.line_id)::int AS line_count,
            count(l.line_id) FILTER(WHERE l.remaining_qty=0)::int AS completed_count,
            count(l.line_id) FILTER(WHERE l.remaining_qty>0)::int AS outstanding_count,
            count(l.line_id) FILTER(WHERE l.remaining_qty IS NULL)::int AS review_count,
            count(l.line_id) FILTER(WHERE l.remaining_qty<0)::int AS excess_count,
            count(l.line_id) FILTER(WHERE l.shared)::int AS shared_count
          FROM summary_base b LEFT JOIN summary_receipt_scope l ON l.gross_id=b.gross_id GROUP BY b.gross_id
        )
        """;

    /// <summary>筛选只展开所选维度；未知代码由应用层拒绝，SQL不拼接用户文本。</summary>
    internal static string Attention(string scope,IReadOnlyCollection<string> codes)
    {
        var parts=new List<string>{Base(scope,codes.Contains("SourceReview"))};
        if(codes.Any(c=>c is "Overdue" or "DeliveryReview")) parts.Add(Delivery);
        if(codes.Any(c=>c=="NoActiveMaterial"||c.StartsWith("Purchase",StringComparison.Ordinal))) parts.Add(Material);
        if(codes.Any(c=>c.StartsWith("Purchase",StringComparison.Ordinal))) parts.Add(Purchase);
        if(codes.Any(c=>c.StartsWith("Receipt",StringComparison.Ordinal))) parts.Add(Receipts);
        var predicates=codes.Distinct().Select(c=>c switch {
          "Overdue"=>"EXISTS(SELECT 1 FROM delivery_facts d WHERE d.gross_id=b.gross_id AND d.is_overdue=TRUE)",
          "MissingRequiredDate"=>"b.required_date IS NULL",
          "SourceReview"=>"b.source_review",
          "DeliveryReview"=>"EXISTS(SELECT 1 FROM delivery_facts d WHERE d.gross_id=b.gross_id AND (d.remaining_qty IS NULL OR d.remaining_qty<0))",
          "NoActiveMaterial"=>"EXISTS(SELECT 1 FROM summary_material_counts m WHERE m.gross_id=b.gross_id AND m.active_count=0)",
          "PurchaseShortfall"=>"EXISTS(SELECT 1 FROM summary_purchase_counts p WHERE p.gross_id=b.gross_id AND p.shortfall_count>0)",
          "PurchaseExcess"=>"EXISTS(SELECT 1 FROM summary_purchase_counts p WHERE p.gross_id=b.gross_id AND p.excess_count>0)",
          "PurchaseReview"=>"EXISTS(SELECT 1 FROM summary_purchase_counts p WHERE p.gross_id=b.gross_id AND p.review_count>0)",
          "ReceiptOutstanding"=>"EXISTS(SELECT 1 FROM summary_receipt_counts r WHERE r.gross_id=b.gross_id AND r.outstanding_count>0)",
          "ReceiptExcess"=>"EXISTS(SELECT 1 FROM summary_receipt_counts r WHERE r.gross_id=b.gross_id AND r.excess_count>0)",
          "ReceiptReview"=>"EXISTS(SELECT 1 FROM summary_receipt_counts r WHERE r.gross_id=b.gross_id AND r.review_count>0)",
          _=>"FALSE"});
        parts.Add("summary_attention AS(SELECT b.gross_id FROM summary_base b WHERE b.lifecycle_status=0 AND ("+string.Join(" OR ",predicates)+"))");
        return string.Join(",",parts);
    }
}
