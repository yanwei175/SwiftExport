using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

/// <summary>采购收货的共同事实：先验证完整入库头，再将未知证据限定到该头关联的采购行。</summary>
internal static class PurchaseReceiptFactsSql
{
    /// <summary>范围为源码生成的单列 uid 查询；保留草稿历史，但草稿不计正式实收，不拼接外部输入。</summary>
    internal static string Ctes(string scope) => $"""
        receipt_scope AS ({scope}), receipt_candidate_heads AS (
            SELECT DISTINCT il.inbound_document_id FROM public.inbound_document_line il
            LEFT JOIN public.inbound_document h ON h.uid=il.inbound_document_id
            WHERE il.source_line_id IN(SELECT uid FROM receipt_scope) AND (h.document_type=10 OR h.uid IS NULL)
        ), receipt_history_bad_heads AS (
            {PostgreSqlInboundReceiptHistoryGuard.InvalidHeadsSql.Replace("@Scoped","TRUE").Replace("=ANY(@PurchaseLineIds)"," IN(SELECT uid FROM receipt_scope)")}
        ), receipt_head_checks AS (
            SELECT il.inbound_document_id,il.uid AS inbound_line_id,il.source_line_id,h.posting_status,il.quantity,
                CASE WHEN h.uid IS NULL THEN '入库单头缺失'
                    WHEN h.posting_status=0 THEN ''
                    WHEN h.is_deleted OR il.is_deleted THEN '入库记录已删除，历史需核实'
                    WHEN pol.uid IS NULL OR poh.uid IS NULL OR pol.is_deleted OR poh.is_deleted THEN '采购目标缺失或已删除'
                    WHEN poh.confirmation_status IS DISTINCT FROM 1 THEN '未确认采购已有正式收货，需核实'
                    WHEN h.posting_status IS NULL OR h.posting_status NOT IN(1,8) THEN '入库状态需核实'
                    WHEN il.quantity<=0 OR il.original_line_id IS NOT NULL THEN '入库数量或历史类型需核实'
                    WHEN h.posted_date IS NULL THEN '过账时间缺失'
                    WHEN h.posting_status=8 AND h.reversed_date IS NULL THEN '反向时间缺失'
                    WHEN NULLIF(btrim(il.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL
                        OR btrim(il.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                        OR il.product_id IS DISTINCT FROM pol.product_id THEN '入库产品或单位需核实'
                    ELSE '' END AS review_reason
            FROM public.inbound_document_line il
            LEFT JOIN public.inbound_document h ON h.uid=il.inbound_document_id
            LEFT JOIN public.purchase_order_line pol ON pol.uid=il.source_line_id
            LEFT JOIN public.purchase_order_head poh ON poh.uid=pol.purchase_order_id
            WHERE il.inbound_document_id IN(SELECT inbound_document_id FROM receipt_candidate_heads)
                AND NOT(COALESCE(h.posting_status,-1)=0 AND (il.is_deleted OR h.is_deleted))
        ), receipt_head_reviews AS (
            SELECT c.inbound_document_id,
                concat_ws('；',string_agg(DISTINCT NULLIF(c.review_reason,''),'；' ORDER BY NULLIF(c.review_reason,'')),
                    CASE WHEN EXISTS(SELECT 1 FROM receipt_history_bad_heads bad WHERE bad.uid=c.inbound_document_id)
                        THEN '正式入库反向历史需核实' END) AS review_reason
            FROM receipt_head_checks c GROUP BY c.inbound_document_id
        ), receipt_evidence AS (
            SELECT c.inbound_line_id,c.source_line_id,c.posting_status,c.quantity,r.review_reason,
                (r.review_reason<>'') AS invalid
            FROM receipt_head_checks c JOIN receipt_head_reviews r ON r.inbound_document_id=c.inbound_document_id
            WHERE c.source_line_id IN(SELECT uid FROM receipt_scope)
        ), receipt_facts AS (
            SELECT source_line_id,SUM(CASE WHEN posting_status=1 THEN quantity ELSE 0 END) AS received_qty,
                MAX(CASE WHEN invalid THEN 1 ELSE 0 END) AS has_unit_mismatch,
                string_agg(DISTINCT NULLIF(review_reason,''),'；' ORDER BY NULLIF(review_reason,'')) AS receipt_review_reason
            FROM receipt_evidence GROUP BY source_line_id
        )
        """;
}
