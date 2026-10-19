using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.OrderTracking.Receipts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

/// <summary>按相关采购行读取整行收货进度及原始证据，不分摊为本需求或物料实收。</summary>
public sealed class PostgreSqlDemandTrackingReceiptRepository(RepositoryBaseContext context) : IDemandTrackingReceiptRepository
{
    /// <summary>需求级范围包含其所有物料历史关联，采购行和入库行均按稳定身份去重。</summary>
    public Task<DemandTrackingReceiptSectionDto> GetReceiptsAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
        =>ReadAsync(grossDemandId,null,cancellationToken);

    /// <summary>物料历史仍可追溯；仅不存在的物料返回null，不隐藏软删除修订。</summary>
    public async Task<DemandTrackingReceiptSectionDto?> GetMaterialReceiptsAsync(Guid netDemandId,CancellationToken cancellationToken=default)
    {
        var grossId=await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT gross_demand_id FROM public.net_demand WHERE uid=@NetDemandId",new{NetDemandId=netDemandId},context.Session.Transaction,cancellationToken:cancellationToken));
        return grossId.HasValue?await ReadAsync(grossId.Value,netDemandId,cancellationToken):null;
    }

    /// <summary>先读所有相关PO行，包含尚无收货的正常行，再限定范围校验历史并解释逐行进度。</summary>
    private async Task<DemandTrackingReceiptSectionDto> ReadAsync(Guid grossDemandId,Guid? netDemandId,CancellationToken cancellationToken)
    {
        var scope=netDemandId.HasValue?"n.uid=@ScopeId":"n.gross_demand_id=@ScopeId";
        var lines=(await context.Session.Connection.QueryAsync<PurchaseLineReceiptProgressDto>(new CommandDefinition($"""
            WITH related AS (
            SELECT l.target_line_id,l.target_head_id,l.target_no FROM public.net_demand n
            JOIN public.demand_execution_link l ON l.net_demand_id=n.uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            WHERE {scope}
            ), targets AS(
            SELECT target_line_id,min(target_head_id::text)::uuid AS head_id,min(target_no COLLATE "C") AS target_no,count(DISTINCT target_head_id) AS head_count
            FROM related GROUP BY target_line_id
            )
            SELECT t.head_id AS "PurchaseOrderId",t.target_line_id AS "PurchaseLineId",COALESCE(poh.order_no,t.target_no) AS "PurchaseOrderNo",
            COALESCE(pol.line_number,0) AS "PurchaseLineNumber",pol.sku AS "Sku",pol.unit_snapshot AS "Unit",
            CASE WHEN t.head_count=1 AND pol.uid IS NOT NULL AND poh.uid IS NOT NULL AND NOT pol.is_deleted AND NOT poh.is_deleted
            AND pol.quantity>0 AND NULLIF(btrim(pol.unit_snapshot,' '),'') IS NOT NULL THEN pol.quantity END AS "PurchaseLineQty",
            EXISTS(SELECT 1 FROM public.demand_execution_link other
            WHERE other.target_line_id=t.target_line_id AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty
            GROUP BY other.target_line_id HAVING count(DISTINCT other.net_demand_id)>1) AS "IsSharedPurchaseLine",
            CASE WHEN t.head_count<>1 THEN '关联采购头身份不一致'
            WHEN pol.uid IS NULL OR poh.uid IS NULL OR pol.is_deleted OR poh.is_deleted THEN '采购目标缺失或已删除'
            WHEN pol.quantity<=0 OR NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL THEN '采购数量或单位需核实'
            ELSE '' END AS "ReviewReason"
            FROM targets t LEFT JOIN public.purchase_order_line pol ON pol.uid=t.target_line_id AND pol.purchase_order_id=t.head_id
            LEFT JOIN public.purchase_order_head poh ON poh.uid=t.head_id ORDER BY poh.order_no NULLS LAST,pol.line_number,t.target_line_id
            """,new{ScopeId=netDemandId??grossDemandId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
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
        return new(){GrossDemandId=grossDemandId,NetDemandId=netDemandId,Items=items,PurchaseLines=lines,
            ReviewReason=JoinReasons(items.Select(item=>item.ReviewReason).Concat(lines.Select(line=>line.ReviewReason)).Append(historyReason))};
    }

    /// <summary>合并已证明的历史及单行原因，不以提示文字决定数量归属。</summary>
    private static string JoinReasons(IEnumerable<string?> reasons)=>string.Join("；",reasons.Where(value=>!string.IsNullOrEmpty(value)).Distinct());
}
