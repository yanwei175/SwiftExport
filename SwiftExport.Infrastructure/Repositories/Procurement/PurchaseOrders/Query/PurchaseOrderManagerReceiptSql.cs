using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>采购管理按相关采购行局部核实正式收货；复用整入库头反向证明，不让无关历史阻断列表。</summary>
internal static class PurchaseOrderManagerReceiptSql
{
    /// <summary>仅接收源码范围谓词，返回每条采购行的已知数量及异常原因；不拼接用户输入。</summary>
    internal static string Ctes(string headScope)=>$"""
        manager_receipt_scope AS (
            SELECT l.uid FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            WHERE l.is_deleted=FALSE AND h.is_deleted=FALSE AND ({headScope})
        ), manager_bad_receipt_heads AS (
            {PostgreSqlInboundReceiptHistoryGuard.InvalidHeadsSql.Replace("@Scoped","TRUE").Replace("=ANY(@PurchaseLineIds)"," IN(SELECT uid FROM manager_receipt_scope)")}
        ), manager_receipt_evidence AS (
            SELECT il.source_line_id,h.posting_status,il.quantity,
                CASE WHEN h.uid IS NULL OR h.is_deleted OR il.is_deleted OR h.posting_status NOT IN(1,8)
                    OR il.quantity<=0 OR il.original_line_id IS NOT NULL OR h.posted_date IS NULL
                    OR (h.posting_status=8 AND h.reversed_date IS NULL)
                    OR NULLIF(btrim(il.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(pl.unit_snapshot,' '),'') IS NULL
                    OR btrim(il.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                    OR il.product_id IS DISTINCT FROM pl.product_id
                    OR EXISTS(SELECT 1 FROM manager_bad_receipt_heads bad WHERE bad.uid=h.uid)
                    OR EXISTS(SELECT 1 FROM public.inbound_document_line sibling
                        LEFT JOIN public.purchase_order_line sibling_target ON sibling_target.uid=sibling.source_line_id
                        WHERE sibling.inbound_document_id=h.uid AND sibling.is_deleted=FALSE AND sibling_target.uid IS NULL)
                    THEN 1 ELSE 0 END AS invalid
            FROM public.inbound_document_line il LEFT JOIN public.inbound_document h ON h.uid=il.inbound_document_id
            JOIN public.purchase_order_line pl ON pl.uid=il.source_line_id
            WHERE il.source_line_id IN(SELECT uid FROM manager_receipt_scope) AND (h.document_type=@PurchaseReceipt OR h.uid IS NULL)
                AND h.posting_status IS DISTINCT FROM 0
        ), receipt_facts AS (
            SELECT source_line_id,SUM(CASE WHEN posting_status=1 THEN quantity ELSE 0 END) AS received_qty,
                MAX(invalid) AS has_unit_mismatch,
                CASE WHEN MAX(invalid)>0 THEN '正式收货或整入库头反向证据需核实' ELSE NULL END AS receipt_review_reason
            FROM manager_receipt_evidence GROUP BY source_line_id
        )
        """;
}
