using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.OrderTracking.Receipts;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;

/// <summary>按相关采购行读取整行收货证据；同一入库行只展示一次，不分摊为需求实收。</summary>
public sealed class PostgreSqlDemandTrackingReceiptRepository(RepositoryBaseContext context) : IDemandTrackingReceiptRepository
{
    /// <summary>仅历史门禁命中转为待核实并保留证据；数据库失败和取消仍原样传播。</summary>
    public async Task<DemandTrackingReceiptSectionDto> GetReceiptsAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
    {
        var scope=(await context.Session.Connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT l.target_line_id FROM public.net_demand n JOIN public.demand_execution_link l ON l.net_demand_id=n.uid
            WHERE n.gross_demand_id=@GrossDemandId AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            """,new{GrossDemandId=grossDemandId},context.Session.Transaction,cancellationToken:cancellationToken))).ToArray();
        var reason=string.Empty;
        try { await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(context.Session,scope,cancellationToken); }
        catch(UnverifiedReceiptHistoryException) { reason="相关正式入库反向历史需核实，整行实收计入状态未知"; }
        var items=(await context.Session.Connection.QueryAsync<DemandTrackingReceiptDto>(new CommandDefinition("""
            SELECT il.inbound_document_id AS "InboundDocumentId",il.uid AS "InboundLineId",h.document_no AS "InboundDocumentNo",
                il.line_number AS "LineNumber",il.source_line_id AS "PurchaseLineId",poh.order_no AS "PurchaseOrderNo",
                COALESCE(pol.line_number,0) AS "PurchaseLineNumber",il.sku_snapshot AS "Sku",il.quantity AS "Qty",il.unit_snapshot AS "Unit",
                COALESCE(h.posting_status,0) AS "PostingStatus",h.receipt_date AS "ReceiptDate",h.posted_date AS "PostedDate",h.reversed_date AS "ReversedDate",
                NULL::boolean AS "CountsTowardReceipt",
                (SELECT count(DISTINCT other.net_demand_id)>1 FROM public.demand_execution_link other
                    WHERE other.target_line_id=il.source_line_id AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                      AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty) AS "IsSharedPurchaseLine",
                CASE WHEN h.uid IS NULL THEN '入库单头缺失'
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
            ORDER BY h.receipt_date DESC NULLS LAST,h.uid,il.line_number,il.uid
            """,PostgreSqlParameters.Ids(scope),context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        foreach(var item in items)
        {
            item.ReviewReason=string.Join("；",new[]{item.ReviewReason,reason}.Where(value=>!string.IsNullOrEmpty(value)));
            item.CountsTowardReceipt=string.IsNullOrEmpty(item.ReviewReason)?item.PostingStatus==InboundPostingStatusEnum.Posted:null;
        }
        return new(){GrossDemandId=grossDemandId,Items=items,ReviewReason=string.Join("；",items.Select(row=>row.ReviewReason).Append(reason).Where(value=>!string.IsNullOrEmpty(value)).Distinct())};
    }
}
