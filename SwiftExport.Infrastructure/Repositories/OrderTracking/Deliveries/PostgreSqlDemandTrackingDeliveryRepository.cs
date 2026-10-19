using Dapper;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using SwiftExport.Core.Interfaces.OrderTracking.Deliveries;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;

/// <summary>只核对本需求箱单交付与反向历史，不读取采购或收货守卫。</summary>
public sealed class PostgreSqlDemandTrackingDeliveryRepository(RepositoryBaseContext context) : IDemandTrackingDeliveryRepository
{
    /// <summary>异常证据仍返回，净交付和未交仅在客户、单位及全部箱单历史可信时计算。</summary>
    public async Task<DemandTrackingDeliverySectionDto> GetDeliveriesAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
    {
        var parameters=new{GrossDemandId=grossDemandId};
        var basis=await context.Session.Connection.QuerySingleOrDefaultAsync<DeliveryBasis>(new CommandDefinition("""
            SELECT demand_qty AS "DemandQty",unit_snapshot AS "Unit",customer_id AS "CustomerId",
                source_uid AS "SourceUid",source_line_id AS "SourceLineId",source_type AS "SourceType",source_no AS "SourceNo",
                source_version AS "SourceVersion",demand_basis_fingerprint AS "DemandBasisFingerprint"
            FROM public.gross_demand WHERE uid=@GrossDemandId AND is_deleted=FALSE
            """,parameters,context.Session.Transaction,cancellationToken:cancellationToken));
        var rows=(await context.Session.Connection.QueryAsync<DeliveryRow>(new CommandDefinition("""
            SELECT pl.packing_list_head_id AS "PackingListId",pl.uid AS "PackingListLineId",ph.packing_list_no AS "PackingListNo",
                pl.line_number AS "LineNumber",pl.qty AS "Qty",pl.unit_snapshot AS "Unit",
                (ph.posting_status=1) AS "CountsTowardDelivery",COALESCE(ph.posting_status,-1) AS "PostingStatus",
                ph.posted_date AS "PostedDate",ph.reversed_date AS "ReversedDate",
                CASE WHEN ph.uid IS NULL THEN '箱单头缺失'
                    WHEN ph.posting_status IN (1,8) AND (ph.is_deleted OR pl.is_deleted) THEN '箱单记录已删除，正式历史需核实'
                    WHEN ph.posting_status NOT IN (0,1,8) THEN '箱单状态需核实'
                    WHEN ph.customer_id IS NULL OR g.customer_id IS NULL OR ph.customer_id<>g.customer_id THEN '交付客户需核实'
                    WHEN rtrim(COALESCE(pl.source_type_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci
                      OR rtrim(COALESCE(pl.source_document_no_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.source_no,' ') COLLATE public.erp_dictionary_ci
                      OR COALESCE(pl.source_line_number_snapshot,-1)<>COALESCE(g.source_line_number,-1)
                      OR rtrim(COALESCE(pl.sku_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.sku_snapshot,' ') COLLATE public.erp_dictionary_ci
                      OR pl.is_non_standard_snapshot<>g.is_non_standard OR pl.product_id IS DISTINCT FROM g.product_id THEN '装箱来源快照需核实'
                    WHEN pl.qty<=0 OR (ph.posting_status IN (1,8) AND ph.posted_date IS NULL)
                      OR (ph.posting_status=8 AND ph.reversed_date IS NULL) THEN '装箱数量或正式历史需核实'
                    WHEN NULLIF(btrim(pl.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(g.unit_snapshot,' '),'') IS NULL
                      OR btrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(g.unit_snapshot,' ') COLLATE public.erp_dictionary_ci THEN '交付单位需核实'
                    ELSE '' END AS "ReviewReason"
            FROM public.packing_list_line pl
            LEFT JOIN public.packing_list_head ph ON ph.uid=pl.packing_list_head_id
            JOIN public.gross_demand g ON g.uid=pl.gross_demand_id AND g.is_deleted=FALSE
            WHERE pl.gross_demand_id=@GrossDemandId
                AND NOT (COALESCE(ph.posting_status,-1)=0 AND (pl.is_deleted OR ph.is_deleted))
            ORDER BY ph.posted_date NULLS LAST,pl.packing_list_head_id,pl.line_number,pl.uid
            """,parameters,context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        // 正常草稿不表示正式交付；异常草稿仍作为证据展示并阻断确定性净额。
        var items=rows.Where(row=>row.PostingStatus!=0||!string.IsNullOrEmpty(row.ReviewReason)).ToList();
        foreach(var item in items) if(!string.IsNullOrEmpty(item.ReviewReason)) item.CountsTowardDelivery=false;
        var reason=GetBasisReason(basis);
        reason=string.Join("；",items.Select(row=>row.ReviewReason).Prepend(reason).Where(value=>!string.IsNullOrEmpty(value)).Distinct());
        decimal? delivered=string.IsNullOrEmpty(reason)?items.Where(row=>row.CountsTowardDelivery).Sum(row=>row.Qty):null;
        return new(){GrossDemandId=grossDemandId,Unit=basis?.Unit!,DeliveredQty=delivered,RemainingQty=delivered.HasValue?basis!.DemandQty-delivered:null,
            ReviewReason=reason,Items=items.Cast<DemandTrackingDeliveryDto>().ToList()};
    }

    /// <summary>来源身份、版本和指纹不完整时不能以没有箱单推导可信零交付。</summary>
    private static string GetBasisReason(DeliveryBasis? basis)
    {
        if(basis is null) return "成品需求已不存在";
        if(basis.CustomerId is null || basis.CustomerId==Guid.Empty) return "来源客户缺失";
        if(basis.SourceUid==Guid.Empty || basis.SourceLineId==Guid.Empty || string.IsNullOrWhiteSpace(basis.SourceNo)
            || basis.SourceType is not ("SO" or "CR" or "MD")
            || (basis.SourceType=="MD"?basis.SourceVersion is not null:basis.SourceVersion is null or <=0)
            || basis.DemandBasisFingerprint is not {Length:64}
            || basis.DemandBasisFingerprint.Any(ch=>!((ch>='0'&&ch<='9')||(ch>='A'&&ch<='F'))))
            return "来源身份、版本或需求指纹需核实";
        return string.IsNullOrWhiteSpace(basis.Unit)?"来源单位缺失":string.Empty;
    }

    /// <summary>只承载本次交付净额依据，不作为对外共享 DTO。</summary>
    private sealed class DeliveryBasis
    {
        public decimal DemandQty {get;set;}
        public string? Unit {get;set;}
        public Guid? CustomerId {get;set;}
        public Guid SourceUid {get;set;}
        public Guid SourceLineId {get;set;}
        public string? SourceType {get;set;}
        public string? SourceNo {get;set;}
        public int? SourceVersion {get;set;}
        public string? DemandBasisFingerprint {get;set;}
    }

    /// <summary>额外保留过账状态以区分正常草稿和必须展示的异常证据。</summary>
    private sealed class DeliveryRow : DemandTrackingDeliveryDto
    {
        public int PostingStatus {get;set;}
    }
}
