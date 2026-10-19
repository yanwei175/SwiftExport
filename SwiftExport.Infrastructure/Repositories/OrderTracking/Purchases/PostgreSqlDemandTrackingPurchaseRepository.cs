using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Core.Interfaces.OrderTracking.Purchases;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Purchases;
/// <summary>一行一个采购 Link，保留历史和缺失目标，不读取收货或分摊整行数量。</summary>
public sealed class PostgreSqlDemandTrackingPurchaseRepository(RepositoryBaseContext context) : IDemandTrackingPurchaseRepository
{
    /// <summary>净贡献按原分配减释放返回；采购头行缺失时整行量未知。</summary>
    public async Task<List<DemandTrackingPurchaseDto>> GetPurchasesAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
        =>(await context.Session.Connection.QueryAsync<DemandTrackingPurchaseDto>(new CommandDefinition($"""
            SELECT l.uid AS "LinkId",n.uid AS "NetDemandId",n.decomposition_revision AS "DecompositionRevision",
                l.target_head_id AS "PurchaseOrderId",l.target_line_id AS "PurchaseLineId",
                (poh.uid IS NOT NULL AND pol.uid IS NOT NULL) AS "HasPurchaseTarget",
                COALESCE(poh.order_no,l.target_no) AS "PurchaseOrderNo",COALESCE(pol.line_number,0) AS "PurchaseLineNumber",
                supplier.supplier_name AS "SupplierName",pol.sku AS "Sku",pol.unit_snapshot AS "Unit",
                COALESCE(poh.confirmation_status,0) AS "ConfirmationStatus",COALESCE(poh.lifecycle_status,9) AS "LifecycleStatus",
                l.allocated_qty AS "AllocatedQty",l.released_qty AS "ReleasedQty",l.allocated_qty-l.released_qty AS "LinkedQty",
                (l.link_status=0 AND NOT l.is_deleted AND l.allocated_qty>l.released_qty) AS "IsActiveLink",
                CASE WHEN poh.uid IS NOT NULL AND pol.uid IS NOT NULL THEN pol.quantity END AS "PurchaseLineQty",
                EXISTS(SELECT 1 FROM public.demand_execution_link other WHERE other.target_line_id=l.target_line_id
                    AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND other.net_demand_id<>n.uid
                    AND other.link_status=0 AND NOT other.is_deleted AND other.allocated_qty>other.released_qty) AS "IsSharedPurchaseLine",
                CASE WHEN l.is_deleted OR l.link_status<>0 THEN '历史采购关联'
                    WHEN l.allocated_qty<=0 OR l.released_qty<0 OR l.released_qty>l.allocated_qty THEN '采购贡献数量需核实'
                    WHEN pol.uid IS NULL OR poh.uid IS NULL THEN '采购目标缺失或已删除'
                    WHEN poh.lifecycle_status=9 THEN '采购已作废'
                    WHEN poh.customer_id IS NULL OR g.customer_id IS NULL OR poh.customer_id<>g.customer_id THEN '客户归属需核实'
                    WHEN pol.product_id IS DISTINCT FROM n.product_id THEN '采购与物料产品不一致'
                    WHEN NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(n.unit_snapshot,' '),'') IS NULL
                      OR btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(n.unit_snapshot,' ') COLLATE public.erp_dictionary_ci THEN '采购与物料单位需核实'
                    ELSE {OrderTrackingEvidenceSql.MaterialReview} END AS "ReviewReason"
            FROM public.net_demand n JOIN public.gross_demand g ON g.uid=n.gross_demand_id AND g.is_deleted=FALSE
            JOIN public.demand_execution_link l ON l.net_demand_id=n.uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
            LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
            LEFT JOIN public.supplier supplier ON supplier.uid=poh.supplier_id AND supplier.is_deleted=FALSE
            WHERE n.gross_demand_id=@GrossDemandId ORDER BY n.decomposition_revision DESC,n.line_number,l.uid
            """,new{GrossDemandId=grossDemandId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
}
