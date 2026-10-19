namespace SwiftExport.Infrastructure.Repositories.OrderTracking;
/// <summary>物料与采购跟踪共用实物依据核实口径，不将技术版本推进误判为拆解失效。</summary>
internal static class OrderTrackingEvidenceSql
{
    internal static string MaterialReview => $"""
        CASE WHEN n.is_deleted OR n.lifecycle_status<>0 THEN '历史物料修订'
            WHEN g.uid IS NULL OR g.is_deleted OR g.lifecycle_status<>0 THEN '来源缺失或非活动需求'
            WHEN NOT COALESCE({SwiftExport.Infrastructure.Repositories.DemandPlanning.MaterialDemandBasisSql.IsApplicable("n","g")},FALSE)
            THEN '来源身份、状态或实物指纹需核实'
            WHEN NULLIF(btrim(n.unit_snapshot,' '),'') IS NULL THEN '物料单位缺失'
            WHEN n.net_qty<=0 THEN '物料需求数量需核实'
            ELSE '' END
        """;
}
