using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Core.Interfaces.OrderTracking.Purchases;
using SwiftExport.Infrastructure.Contexts;
namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Purchases;
/// <summary>采购关联事实与单物料承接进度独立于收货，不虚构整行分配。</summary>
public sealed class PostgreSqlDemandTrackingPurchaseRepository(RepositoryBaseContext context) : IDemandTrackingPurchaseRepository
{
    /// <summary>需求范围保留历史及缺目标关联，一行一个Link。</summary>
    public Task<List<DemandTrackingPurchaseDto>> GetPurchasesAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
        =>ReadItemsAsync(grossDemandId,false,cancellationToken);

    /// <summary>只汇总当前物料可信活动贡献；正常历史及全部释放的关联仅展示，不污染当前承接。</summary>
    public async Task<MaterialTrackingPurchaseSectionDto?> GetMaterialPurchasesAsync(Guid netDemandId,CancellationToken cancellationToken=default)
    {
        var result=await context.Session.Connection.QuerySingleOrDefaultAsync<MaterialTrackingPurchaseSectionDto>(new CommandDefinition($"""
            WITH evidence AS (
                SELECT l.link_status=0 AND NOT l.is_deleted
                    AND (l.allocated_qty<>l.released_qty OR l.allocated_qty<=0 OR l.released_qty<0) AS is_current,
                    l.allocated_qty-l.released_qty AS qty,{OrderTrackingPurchaseSql.Review} AS review_reason
                {OrderTrackingPurchaseSql.From} WHERE n.uid=@NetDemandId
            )
            SELECT n.uid AS "NetDemandId",n.gross_demand_id AS "GrossDemandId",n.net_qty AS "NetQty",n.unit_snapshot AS "Unit",
                CASE WHEN ({OrderTrackingEvidenceSql.MaterialReview})=''
                    AND NOT EXISTS(SELECT 1 FROM evidence WHERE is_current AND review_reason<>'')
                    THEN COALESCE((SELECT sum(qty) FROM evidence WHERE is_current),0) END AS "AssociatedPurchaseQty",
                concat_ws('；',NULLIF(({OrderTrackingEvidenceSql.MaterialReview}),''),
                    (SELECT string_agg(DISTINCT review_reason,'；') FROM evidence WHERE is_current AND review_reason<>'')) AS "ReviewReason"
            FROM public.net_demand n LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id WHERE n.uid=@NetDemandId
            """,new{NetDemandId=netDemandId},context.Session.Transaction,cancellationToken:cancellationToken));
        if(result is null)return null;
        result.Items=await ReadItemsAsync(netDemandId,true,cancellationToken);
        if(result.AssociatedPurchaseQty.HasValue)
        {
            result.RemainingToPurchaseQty=result.NetQty-result.AssociatedPurchaseQty;
            if(result.RemainingToPurchaseQty<0)result.ReviewReason=string.Join("；",new[]{result.ReviewReason,"采购承接超出物料需求量"}.Where(value=>!string.IsNullOrEmpty(value)));
        }
        return result;
    }

    /// <summary>内部范围只有需求或单物料两种固定谓词，不接受外部SQL或筛选。</summary>
    private async Task<List<DemandTrackingPurchaseDto>> ReadItemsAsync(Guid scopeId,bool material,CancellationToken cancellationToken)
    {
        var scope=material?"n.uid=@ScopeId":"n.gross_demand_id=@ScopeId AND g.is_deleted=FALSE";
        return (await context.Session.Connection.QueryAsync<DemandTrackingPurchaseDto>(new CommandDefinition($"""
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
                {OrderTrackingPurchaseSql.Review} AS "ReviewReason"
            FROM public.net_demand n LEFT JOIN public.gross_demand g ON g.uid=n.gross_demand_id
            JOIN public.demand_execution_link l ON l.net_demand_id=n.uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
            LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
            LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
            LEFT JOIN public.supplier supplier ON supplier.uid=poh.supplier_id AND supplier.is_deleted=FALSE
            WHERE {scope} ORDER BY n.decomposition_revision DESC,n.line_number,l.uid
            """,new{ScopeId=scopeId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
    }
}
