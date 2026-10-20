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
            LEFT JOIN public.purchase_order_head relatedHead ON relatedHead.uid=l.target_head_id
            WHERE {scope} AND {OrderTrackingReceiptScopeSql.Include("relatedHead","l.target_line_id")}
            ), targets AS(
            SELECT target_line_id,min(target_head_id::text)::uuid AS head_id,min(target_no COLLATE "C") AS target_no,count(DISTINCT target_head_id) AS head_count
            FROM related GROUP BY target_line_id
            )
            SELECT t.head_id AS "PurchaseOrderId",t.target_line_id AS "PurchaseLineId",COALESCE(poh.order_no,t.target_no) AS "PurchaseOrderNo",
            COALESCE(pol.line_number,0) AS "PurchaseLineNumber",pol.sku AS "Sku",pol.unit_snapshot AS "Unit",
            CASE WHEN t.head_count=1 AND pol.uid IS NOT NULL AND poh.uid IS NOT NULL AND NOT pol.is_deleted AND NOT poh.is_deleted
            AND poh.confirmation_status=1 AND pol.quantity>0 AND NULLIF(btrim(pol.unit_snapshot,' '),'') IS NOT NULL THEN pol.quantity END AS "PurchaseLineQty",
            EXISTS(SELECT 1 FROM public.demand_execution_link other
            WHERE other.target_line_id=t.target_line_id AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty
            GROUP BY other.target_line_id HAVING count(DISTINCT other.net_demand_id)>1) AS "IsSharedPurchaseLine",
            CASE WHEN t.head_count<>1 THEN '关联采购头身份不一致'
            WHEN pol.uid IS NULL OR poh.uid IS NULL OR pol.is_deleted OR poh.is_deleted THEN '采购目标缺失或已删除'
            WHEN poh.confirmation_status<>1 THEN '未确认采购已有入库证据，需核实'
            WHEN pol.quantity<=0 OR NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL THEN '采购数量或单位需核实'
            ELSE '' END AS "ReviewReason"
            FROM targets t LEFT JOIN public.purchase_order_line pol ON pol.uid=t.target_line_id AND pol.purchase_order_id=t.head_id
            LEFT JOIN public.purchase_order_head poh ON poh.uid=t.head_id ORDER BY poh.order_no NULLS LAST,pol.line_number,t.target_line_id
            """,new{ScopeId=netDemandId??grossDemandId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        var evidence=await PurchaseLineReceiptEvidenceReader.ReadAsync(context,lines,cancellationToken);
        return new(){GrossDemandId=grossDemandId,NetDemandId=netDemandId,Items=evidence.Items,PurchaseLines=lines,ReviewReason=evidence.ReviewReason};
    }

}
