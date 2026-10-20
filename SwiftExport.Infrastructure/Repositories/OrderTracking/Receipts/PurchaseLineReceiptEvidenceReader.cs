using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

/// <summary>按已确定采购行读取收货证据与整行净额；需求与采购单视角共用同一核实口径。</summary>
internal static class PurchaseLineReceiptEvidenceReader
{
    /// <summary>借用请求会话，补齐采购行进度；不按需求贡献分摊整行收货。</summary>
    internal static async Task<(List<DemandTrackingReceiptDto> Items,string ReviewReason)> ReadAsync(
        RepositoryBaseContext context,List<PurchaseLineReceiptProgressDto> lines,CancellationToken cancellationToken)
    {
        var ids=lines.Select(line=>line.PurchaseLineId).ToArray();
        var historyReason=string.Empty;
        try{await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(context.Session,ids,cancellationToken);}
        catch(UnverifiedReceiptHistoryException){historyReason="相关正式入库反向历史需核实，整行实收计入状态未知";}
        var items=(await context.Session.Connection.QueryAsync<DemandTrackingReceiptDto>(new CommandDefinition("""
            SELECT il.inbound_document_id AS "InboundDocumentId",il.uid AS "InboundLineId",h.document_no AS "InboundDocumentNo",
                il.line_number AS "LineNumber",il.source_line_id AS "PurchaseLineId",poh.order_no AS "PurchaseOrderNo",
                COALESCE(pol.line_number,0) AS "PurchaseLineNumber",il.sku_snapshot AS "Sku",il.quantity AS "Qty",il.unit_snapshot AS "Unit",
                COALESCE(h.posting_status,0) AS "PostingStatus",h.receipt_date AS "ReceiptDate",h.posted_date AS "PostedDate",h.reversed_date AS "ReversedDate",
                CASE WHEN h.posting_status=0 THEN FALSE ELSE NULL::boolean END AS "CountsTowardReceipt",
                (SELECT count(DISTINCT other.net_demand_id)>1 FROM public.demand_execution_link other
                    WHERE other.target_line_id=il.source_line_id AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                      AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty) AS "IsSharedPurchaseLine",
                CASE WHEN h.uid IS NULL THEN '入库单头缺失'
                    WHEN h.posting_status=0 THEN ''
                    WHEN h.is_deleted OR il.is_deleted THEN '入库记录已删除，历史需核实'
                    WHEN pol.uid IS NULL OR poh.uid IS NULL THEN '采购来源缺失'
                    WHEN poh.confirmation_status<>1 THEN '未确认采购已有正式收货，需核实'
                    WHEN h.posting_status NOT IN (0,1,8) THEN '入库状态需核实'
                    WHEN il.quantity<=0 OR il.original_line_id IS NOT NULL THEN '入库数量或历史类型需核实'
                    WHEN h.posting_status IN (1,8) AND h.posted_date IS NULL THEN '过账时间缺失'
                    WHEN h.posting_status=8 AND h.reversed_date IS NULL THEN '反向时间缺失'
                    WHEN NULLIF(btrim(il.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL
                      OR btrim(il.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                      OR il.product_id IS DISTINCT FROM pol.product_id THEN '入库产品或单位需核实'
                    ELSE '' END AS "ReviewReason"
            FROM public.inbound_document_line il
            LEFT JOIN public.inbound_document h ON h.uid=il.inbound_document_id
            LEFT JOIN public.purchase_order_line pol ON pol.uid=il.source_line_id
            LEFT JOIN public.purchase_order_head poh ON poh.uid=pol.purchase_order_id
                WHERE il.source_line_id=ANY(@Uids) AND (h.document_type=10 OR h.uid IS NULL)
                    AND NOT (COALESCE(h.posting_status,-1)=0 AND (il.is_deleted OR h.is_deleted))
            ORDER BY h.receipt_date DESC NULLS LAST,h.uid,il.line_number,il.uid
            """,PostgreSqlParameters.Ids(ids),context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        foreach(var item in items)
        {
            // SQL只为确实存在的未过账头返回false；缺头默认状态0仍是null，不能误当草稿。
            if((int)item.PostingStatus==0&&item.CountsTowardReceipt==false)continue;
            item.ReviewReason=JoinReasons(new[]{item.ReviewReason,historyReason});
            item.CountsTowardReceipt=string.IsNullOrEmpty(item.ReviewReason)?item.PostingStatus==InboundPostingStatusEnum.Posted:null;
        }
        foreach(var line in lines)
        {
            // 草稿提示留在证据中，但尚未发生的草稿不能污染正式净额的可信度。
            var evidence=items.Where(item=>item.PurchaseLineId==line.PurchaseLineId
                &&!((int)item.PostingStatus==0&&item.CountsTowardReceipt==false)).ToArray();
            line.ReviewReason=JoinReasons(evidence.Select(item=>item.ReviewReason).Append(line.ReviewReason).Append(historyReason));
            if(string.IsNullOrEmpty(line.ReviewReason)&&line.PurchaseLineQty.HasValue)
            {
                line.ReceivedQty=evidence.Where(item=>item.CountsTowardReceipt==true).Sum(item=>item.Qty);
                line.RemainingQty=line.PurchaseLineQty-line.ReceivedQty;
                if(line.RemainingQty<0)line.ReviewReason="采购行超收，未收量为负";
            }
        }
        return (items,JoinReasons(items.Select(item=>item.ReviewReason).Concat(lines.Select(line=>line.ReviewReason)).Append(historyReason)));
    }

    /// <summary>合并已证明的历史与单行原因，不以提示文字决定数量归属。</summary>
    private static string JoinReasons(IEnumerable<string?> reasons)=>string.Join("；",reasons.Where(value=>!string.IsNullOrEmpty(value)).Distinct());
}
