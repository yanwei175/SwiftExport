namespace SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;

/// <summary>主表逾期选组与交付明细共用同一批量证据SQL，不逐需求调用明细。</summary>
internal static class OrderTrackingDeliverySql
{
    /// <summary>仅接受内部范围谓词；业务日由外层一次取得并参数化。</summary>
    internal static string Ctes(string scope) => $"""
        delivery_basis AS (
            SELECT g.uid AS gross_id,g.demand_qty,g.unit_snapshot AS unit,g.required_date,g.lifecycle_status,
                CASE WHEN g.customer_id IS NULL OR g.customer_id='00000000-0000-0000-0000-000000000000'::uuid THEN '来源客户缺失'
                    WHEN g.source_uid IS NULL OR g.source_uid='00000000-0000-0000-0000-000000000000'::uuid
                      OR g.source_line_id IS NULL OR g.source_line_id='00000000-0000-0000-0000-000000000000'::uuid
                      OR (g.source_no IS NULL OR btrim(g.source_no,chr(9)||chr(10)||chr(11)||chr(12)||chr(13)||chr(32)||chr(133)||chr(160)||chr(5760)||chr(8192)||chr(8193)||chr(8194)||chr(8195)||chr(8196)||chr(8197)||chr(8198)||chr(8199)||chr(8200)||chr(8201)||chr(8202)||chr(8232)||chr(8233)||chr(8239)||chr(8287)||chr(12288))='')
                      OR g.source_type IS NULL OR g.source_type COLLATE "C" NOT IN ('SO','CR','MD')
                      OR (g.source_type COLLATE "C"='MD' AND g.source_version IS NOT NULL)
                      OR (g.source_type COLLATE "C"<>'MD' AND (g.source_version IS NULL OR g.source_version<=0))
                      OR g.demand_basis_fingerprint IS NULL OR char_length(g.demand_basis_fingerprint)<>64
                      OR g.demand_basis_fingerprint COLLATE "C" ~ '[^0-9A-F]' THEN '来源身份、版本或需求指纹需核实'
                    WHEN (g.unit_snapshot IS NULL OR btrim(g.unit_snapshot,chr(9)||chr(10)||chr(11)||chr(12)||chr(13)||chr(32)||chr(133)||chr(160)||chr(5760)||chr(8192)||chr(8193)||chr(8194)||chr(8195)||chr(8196)||chr(8197)||chr(8198)||chr(8199)||chr(8200)||chr(8201)||chr(8202)||chr(8232)||chr(8233)||chr(8239)||chr(8287)||chr(12288))='') THEN '来源单位缺失' ELSE '' END AS basis_reason
            FROM public.gross_demand g WHERE g.is_deleted=FALSE AND ({scope})
        ),
        delivery_evidence AS (
            SELECT g.uid AS gross_id,pl.packing_list_head_id AS head_id,pl.uid AS line_id,ph.packing_list_no AS document_no,
                pl.line_number,pl.qty,pl.unit_snapshot AS unit,ph.posting_status,ph.posted_date,ph.reversed_date,
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
                    ELSE '' END AS review_reason
            FROM delivery_basis b JOIN public.gross_demand g ON g.uid=b.gross_id
            JOIN public.packing_list_line pl ON pl.gross_demand_id=g.uid
            LEFT JOIN public.packing_list_head ph ON ph.uid=pl.packing_list_head_id
            WHERE NOT (COALESCE(ph.posting_status,-1)=0 AND (pl.is_deleted OR ph.is_deleted))
        ),
        delivery_checked AS (
            SELECT b.gross_id,b.demand_qty,b.unit,b.required_date,b.lifecycle_status,
                concat_ws('；',NULLIF(b.basis_reason,''),string_agg(DISTINCT NULLIF(e.review_reason,''),'；')) AS quantity_reason,
                COALESCE(sum(CASE WHEN e.posting_status=1 AND e.review_reason='' THEN e.qty ELSE 0 END),0) AS raw_delivered
            FROM delivery_basis b LEFT JOIN delivery_evidence e ON e.gross_id=b.gross_id
            GROUP BY b.gross_id,b.demand_qty,b.unit,b.required_date,b.lifecycle_status,b.basis_reason
        ),
        delivery_facts AS (
            SELECT gross_id,unit,@BusinessDate AS business_date,
                CASE WHEN quantity_reason='' THEN raw_delivered END AS delivered_qty,
                CASE WHEN quantity_reason='' THEN demand_qty-raw_delivered END AS remaining_qty,
                CASE WHEN quantity_reason<>'' OR required_date IS NULL THEN NULL
                    ELSE lifecycle_status=0 AND required_date<@BusinessDate AND demand_qty-raw_delivered>0 END AS is_overdue,
                CASE WHEN quantity_reason<>'' OR required_date IS NULL THEN NULL
                    WHEN lifecycle_status=0 AND required_date<@BusinessDate AND demand_qty-raw_delivered>0
                    THEN @BusinessDate::date-required_date::date ELSE 0 END AS overdue_days,
                concat_ws('；',NULLIF(quantity_reason,''),CASE WHEN required_date IS NULL THEN '要求交期缺失，逾期状态待核实' END) AS review_reason
            FROM delivery_checked
        )
        """;
}
