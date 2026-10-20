namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>整单删除资格共用持久历史证据；取消确认清空当前标识后仍不能掩盖曾确认事实。</summary>
internal static class PurchaseDraftDeletionSql
{
    /// <summary>以头身份读取确认回执、审计和旧确认实体；不排除已删除历史。</summary>
    internal static string ConfirmationHistory(string headId) => $"""
        (EXISTS(SELECT 1 FROM public.command_receipt receipt WHERE receipt.request_target_uid={headId}
            AND receipt.command_type='Procurement.Order.Confirm')
        OR EXISTS(SELECT 1 FROM public.business_operation_log history WHERE history.aggregate_uid={headId}
            AND history.aggregate_type='PurchaseOrder' AND history.operation_type IN('Confirm','CancelConfirmation'))
        OR EXISTS(SELECT 1 FROM public.purchase_confirmation_line confirmation WHERE confirmation.purchase_order_id={headId}))
        """;

    /// <summary>保留所有入库、反馈、箱单执行及可归属的孤立流水历史，软删除和反向都不抹除曾发生事实。</summary>
    internal static string DownstreamHistory(string headId) => $"""
        (EXISTS(SELECT 1 FROM public.inbound_document_line inbound JOIN public.purchase_order_line line
            ON line.uid=inbound.source_line_id WHERE line.purchase_order_id={headId})
        OR EXISTS(SELECT 1 FROM public.supplier_progress_report report JOIN public.purchase_order_line line
            ON line.uid=report.purchase_order_line_id WHERE line.purchase_order_id={headId})
        OR EXISTS(SELECT 1 FROM public.packing_list_posting_execution_line execution JOIN public.purchase_order_line line
            ON line.uid=execution.purchase_line_id WHERE line.purchase_order_id={headId})
        OR EXISTS(SELECT 1 FROM public.inventory_ledger_entry ledger WHERE ledger.source_document_id={headId}
            OR ledger.source_line_id IN(SELECT uid FROM public.purchase_order_line WHERE purchase_order_id={headId})
            OR ledger.source_line_id IN(SELECT inbound.uid FROM public.inbound_document_line inbound JOIN public.purchase_order_line line
                ON line.uid=inbound.source_line_id WHERE line.purchase_order_id={headId})
            OR ledger.source_document_id IN(SELECT inbound.inbound_document_id FROM public.inbound_document_line inbound
                JOIN public.purchase_order_line line ON line.uid=inbound.source_line_id WHERE line.purchase_order_id={headId})))
        """;

    /// <summary>查询只提供操作提示；提交仍在稳定锁及事务内重验全部历史和引用。</summary>
    internal static string BlockReason(string head) => $"""
        CASE WHEN {head}.is_deleted OR {head}.lifecycle_status<>0 OR {head}.confirmation_status<>0 THEN '仅活动待确认草稿允许删除'
            WHEN {head}.active_confirmation_id IS NOT NULL OR {head}.confirmed_date IS NOT NULL OR {ConfirmationHistory(head + ".uid")}
                THEN '采购曾确认，请按取消确认或作废保留历史'
            WHEN {DownstreamHistory(head + ".uid")}
                THEN '采购存在下游历史，不能删除草稿'
            ELSE '' END
        """;
}
