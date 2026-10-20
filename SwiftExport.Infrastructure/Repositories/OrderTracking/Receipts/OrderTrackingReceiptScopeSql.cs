namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

/// <summary>订单跟踪正式收货范围：纯采购草稿没有待收量，草稿却已有入库证据必须保留核实。</summary>
internal static class OrderTrackingReceiptScopeSql
{
    /// <summary>调用方传固定表别名与采购行表达式，不接收用户SQL；异常确认状态同样留在核实范围。</summary>
    internal static string Include(string head,string lineId)=>$"""
        ({head}.confirmation_status IS DISTINCT FROM 0 OR EXISTS(
            SELECT 1 FROM public.inbound_document_line draftReceipt LEFT JOIN public.inbound_document draftHead
                ON draftHead.uid=draftReceipt.inbound_document_id
            WHERE draftReceipt.source_line_id={lineId} AND (draftHead.document_type=10 OR draftHead.uid IS NULL)
                AND (draftHead.posting_status IS DISTINCT FROM 0 OR
                    (NOT draftReceipt.is_deleted AND NOT draftHead.is_deleted AND draftHead.lifecycle_status=0))))
        """;
}
