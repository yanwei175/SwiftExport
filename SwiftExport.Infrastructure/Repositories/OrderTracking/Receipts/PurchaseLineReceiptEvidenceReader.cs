using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

/// <summary>按已确定采购行读取收货证据与整行净额；需求与采购单视角共用同一核实口径。</summary>
internal static class PurchaseLineReceiptEvidenceReader
{
    /// <summary>借用请求会话，补齐采购行进度；不按需求贡献分摊整行收货。</summary>
    internal static async Task<(List<DemandTrackingReceiptDto> Items,string ReviewReason)> ReadAsync(
        RepositoryBaseContext context,List<PurchaseLineReceiptProgressDto> lines,CancellationToken cancellationToken)
    {
        var ids=lines.Select(line=>line.PurchaseLineId).ToArray();
        var items=(await context.Session.Connection.QueryAsync<DemandTrackingReceiptDto>(new CommandDefinition($"""
            WITH {PurchaseReceiptFactsSql.Ctes("SELECT unnest(@Uids) AS uid")}
            SELECT il.inbound_document_id AS "InboundDocumentId",il.uid AS "InboundLineId",h.document_no AS "InboundDocumentNo",
                il.line_number AS "LineNumber",il.source_line_id AS "PurchaseLineId",poh.order_no AS "PurchaseOrderNo",
                COALESCE(pol.line_number,0) AS "PurchaseLineNumber",il.sku_snapshot AS "Sku",il.quantity AS "Qty",il.unit_snapshot AS "Unit",
                COALESCE(h.posting_status,0) AS "PostingStatus",h.receipt_date AS "ReceiptDate",h.posted_date AS "PostedDate",h.reversed_date AS "ReversedDate",
                CASE WHEN e.invalid THEN NULL::boolean ELSE h.posting_status=1 END AS "CountsTowardReceipt",
                (SELECT count(DISTINCT other.net_demand_id)>1 FROM public.demand_execution_link other
                    WHERE other.target_line_id=il.source_line_id AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                      AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty) AS "IsSharedPurchaseLine",
                e.review_reason AS "ReviewReason"
            FROM public.inbound_document_line il
            JOIN receipt_evidence e ON e.inbound_line_id=il.uid
            LEFT JOIN public.inbound_document h ON h.uid=il.inbound_document_id
            LEFT JOIN public.purchase_order_line pol ON pol.uid=il.source_line_id
            LEFT JOIN public.purchase_order_head poh ON poh.uid=pol.purchase_order_id
            ORDER BY h.receipt_date DESC NULLS LAST,h.uid,il.line_number,il.uid
            """,PostgreSqlParameters.Ids(ids),context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        foreach(var line in lines)
        {
            // 草稿提示留在证据中，但尚未发生的草稿不能污染正式净额的可信度。
            var evidence=items.Where(item=>item.PurchaseLineId==line.PurchaseLineId
                &&!((int)item.PostingStatus==0&&item.CountsTowardReceipt==false)).ToArray();
            line.ReviewReason=JoinReasons(evidence.Select(item=>item.ReviewReason).Append(line.ReviewReason));
            if(string.IsNullOrEmpty(line.ReviewReason)&&line.PurchaseLineQty.HasValue)
            {
                line.ReceivedQty=evidence.Where(item=>item.CountsTowardReceipt==true).Sum(item=>item.Qty);
                line.RemainingQty=line.PurchaseLineQty-line.ReceivedQty;
                if(line.RemainingQty<0)line.ReviewReason="采购行超收，未收量为负";
            }
        }
        return (items,JoinReasons(items.Select(item=>item.ReviewReason).Concat(lines.Select(line=>line.ReviewReason))));
    }

    /// <summary>合并已证明的历史与单行原因，不以提示文字决定数量归属。</summary>
    private static string JoinReasons(IEnumerable<string?> reasons)=>string.Join("；",reasons.Where(value=>!string.IsNullOrEmpty(value)).Distinct());
}
