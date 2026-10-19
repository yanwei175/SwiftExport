namespace SwiftExport.Infrastructure.Repositories.OrderTracking;
/// <summary>新跟踪物料与采购共用来源修订核实口径，不改旧 Trace 的 SQL 或消费者。</summary>
internal static class OrderTrackingEvidenceSql
{
    internal const string MaterialReview = """
        CASE WHEN n.is_deleted OR n.lifecycle_status<>0 THEN '历史物料修订'
            WHEN n.source_demand_version_snapshot IS DISTINCT FROM g.version
              OR NULLIF(rtrim(g.demand_basis_fingerprint,' '),'') IS NULL
              OR n.source_demand_basis_fingerprint_snapshot IS NULL
              OR rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci
                 <>rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
              OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='MD' AND (g.source_version IS NOT NULL OR n.source_version_snapshot IS NOT NULL))
              OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci<>'MD' AND (g.source_version IS NULL OR n.source_version_snapshot IS DISTINCT FROM g.source_version))
            THEN '来源版本或需求指纹需核实'
            WHEN NULLIF(btrim(n.unit_snapshot,' '),'') IS NULL THEN '物料单位缺失'
            WHEN n.net_qty<=0 THEN '物料需求数量需核实'
            ELSE '' END
        """;
}
