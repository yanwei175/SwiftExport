namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

/// <summary>物料管理、订单跟踪及采购追踪共用实物依据判断；技术版本只用于命令并发，不单独否决既有拆解。</summary>
internal static class MaterialDemandBasisSql
{
    /// <summary>仅接收源码中的表别名，检查来源身份、活动状态及完整指纹精确相等，不拼接外部输入。</summary>
    internal static string IsApplicable(string materialAlias,string sourceAlias)=>Predicate
        .Replace("material.",materialAlias+".").Replace("source.",sourceAlias+".");

    private const string Predicate="""
        (material.is_deleted=FALSE AND material.lifecycle_status=0
         AND source.uid IS NOT NULL AND source.is_deleted=FALSE AND source.lifecycle_status=0
         AND source.customer_id IS NOT NULL AND source.customer_id<>'00000000-0000-0000-0000-000000000000'::uuid
         AND source.source_uid IS NOT NULL AND source.source_uid<>'00000000-0000-0000-0000-000000000000'::uuid
         AND source.source_line_id IS NOT NULL AND source.source_line_id<>'00000000-0000-0000-0000-000000000000'::uuid
         AND NULLIF(btrim(source.source_no,E' \t\r\n'),'') IS NOT NULL
         AND rtrim(source.source_type,' ') COLLATE "C" IN ('SO','CR','MD')
         AND ((rtrim(source.source_type,' ') COLLATE "C"='MD' AND source.source_version IS NULL)
           OR (rtrim(source.source_type,' ') COLLATE "C" IN ('SO','CR') AND source.source_version>0))
         AND NULLIF(btrim(source.demand_basis_fingerprint,E' \t\r\n'),'') IS NOT NULL
         AND material.source_demand_basis_fingerprint_snapshot COLLATE "C"=source.demand_basis_fingerprint COLLATE "C")
        """;
}
