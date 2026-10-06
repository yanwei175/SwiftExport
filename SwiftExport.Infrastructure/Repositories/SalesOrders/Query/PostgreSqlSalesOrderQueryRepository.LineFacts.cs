namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>销售专用PG行事实SQL，保留源可信性、原因优先级及正式交付口径，不与需求Trace统一规则。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository
{
    // 源LEN/BIN2组合拒绝尾空格、小写及非ASCII字符，局部正则只承担同一有限格式检查。
    private const string InvalidFingerprint = "g.demand_basis_fingerprint COLLATE \"C\" !~ '^[0-9A-F]{64}$'";
    /// <summary>在已冻结候选头上一次读取当前销售行、来源与装箱历史；来源/历史校验仍在SQL中计算。</summary>
    private static string CreateLineFactsSql() => $"""
        WITH candidate AS ({CandidateRelation})
        SELECT l.sales_order_id AS "SalesOrderId",l.uid AS "SalesOrderLineId",l.line_number AS "LineNumber",
               l.qty AS "Qty",l.unit_snapshot AS "UnitSnapshot",
               COALESCE(l.expected_delivery_date,h.delivery_date)::date::timestamp without time zone AS "EffectiveDueDate",
               delivery.shipped_qty AS "ShippedQty",
               CASE WHEN delivery.shipped_qty IS NULL THEN NULL::numeric
                    WHEN delivery.shipped_qty>=l.qty THEN 0::numeric(18,4)
                    ELSE l.qty-delivery.shipped_qty END AS "UnshippedQty",reason.review_reason AS "ReviewReason"
        FROM candidate h JOIN {Lines} l ON l.sales_order_id=h.uid AND l.is_deleted=FALSE
        LEFT JOIN LATERAL (
            SELECT ranked.*,CASE WHEN ranked.active_count>0 THEN ranked.active_count ELSE ranked.all_count END AS candidate_count
            FROM (
                SELECT g.*,SUM(CASE WHEN g.lifecycle_status=@DemandActive THEN 1 ELSE 0 END) OVER () AS active_count,
                       COUNT(*) OVER () AS all_count
                FROM {Gross} g WHERE NOT ({Different("g.source_type", "@SalesSourceType")})
                  AND g.source_uid=h.uid AND g.source_line_id=l.uid AND g.is_deleted=FALSE
                  AND g.lifecycle_status IN (@DemandActive,@DemandVoided)
            ) ranked
            ORDER BY CASE WHEN ranked.lifecycle_status=@DemandActive THEN 0 ELSE 1 END,
                     ranked.source_version DESC NULLS LAST,{SourceUuidOrder("ranked.uid")} LIMIT 1
        ) g ON TRUE
        LEFT JOIN LATERAL (
            SELECT COALESCE(SUM(CASE WHEN pl.is_deleted=FALSE AND ph.is_deleted=FALSE AND ph.posting_status=@Posted
                                     THEN pl.qty ELSE 0 END),0) AS delivered_qty
            FROM {PackingLines} pl LEFT JOIN {PackingHeads} ph ON ph.uid=pl.packing_list_head_id
            WHERE pl.gross_demand_id=g.uid
        ) packing ON TRUE
        LEFT JOIN LATERAL (
            SELECT COUNT(*) AS invalid_history_count
            FROM {PackingLines} pl LEFT JOIN {PackingHeads} ph ON ph.uid=pl.packing_list_head_id
            WHERE pl.gross_demand_id=g.uid AND (ph.uid IS NULL
                OR (pl.is_deleted=TRUE AND ph.posting_status=@Posted)
                OR (pl.is_deleted=FALSE AND (ph.is_deleted=TRUE
                    OR ph.posting_status NOT IN (@Unposted,@Posted,@Reversed)
                    OR ph.customer_id<>h.customer_id
                    OR (ph.posting_status=@Posted AND ph.posted_date IS NULL)
                    OR (ph.posting_status=@Reversed AND ph.reversed_date IS NULL)
                    OR pl.qty<=0 OR NULLIF(btrim(pl.unit_snapshot,' '),'') IS NULL
                    OR {Different("pl.unit_snapshot", "l.unit_snapshot")}
                    OR pl.is_non_standard_snapshot<>l.is_non_standard
                    OR COALESCE(pl.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                       <>COALESCE(l.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                    OR {Different("pl.sku_snapshot", "l.sku")}
                    OR {Different("pl.source_type_snapshot", "@SalesSourceType")}
                    OR {Different("pl.source_document_no_snapshot", "h.order_no")}
                    OR pl.source_line_number_snapshot IS NULL OR pl.source_line_number_snapshot<>l.line_number)))
        ) history ON TRUE
        CROSS JOIN LATERAL (
            SELECT CASE
                WHEN h.document_status=@OrderOpen AND h.confirmation_status=@Pending AND g.uid IS NULL THEN TRUE
                WHEN h.confirmation_status<>@Confirmed THEN FALSE
                WHEN g.uid IS NULL OR g.candidate_count<>1 THEN FALSE
                WHEN g.lifecycle_status<>@DemandActive
                     AND (h.document_status<>@OrderClosed OR g.lifecycle_status<>@DemandVoided) THEN FALSE
                WHEN {Different("g.source_type", "@SalesSourceType")} OR g.source_uid<>h.uid
                  OR {Different("g.source_no", "h.order_no")}
                  OR h.customer_id IS NULL OR g.customer_id IS NULL OR g.customer_id<>h.customer_id
                  OR g.source_line_number IS NULL OR g.source_line_number<>l.line_number
                  OR g.is_non_standard<>l.is_non_standard
                  OR COALESCE(g.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                     <>COALESCE(l.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                  OR {Different("g.sku_snapshot", "l.sku")}
                  OR {Different("COALESCE(g.link_sku_snapshot,'')", "COALESCE(NULLIF(btrim(l.link_sku,' '),''),'')")}
                  OR {Different("COALESCE(g.customer_sku_snapshot,'')", "COALESCE(NULLIF(btrim(l.customer_sku,' '),''),'')")}
                  OR {Different("COALESCE(g.description_snapshot,'')", "COALESCE(NULLIF(btrim(l.description_en,' '),''),'')")}
                  OR {Different("COALESCE(g.color_snapshot,'')", "COALESCE(NULLIF(btrim(l.color_name_en,' '),''),'')")}
                  OR {Different("COALESCE(g.specification_snapshot,'')", "COALESCE(NULLIF(btrim(l.features_en,' '),''),'')")}
                  OR {Different("COALESCE(g.image_path_snapshot,'')", "COALESCE(NULLIF(btrim(l.main_image,' '),''),'')")}
                  OR {Different("COALESCE(g.remark,'')", "COALESCE(NULLIF(btrim(l.sub_note,' '),''),'')")}
                  OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL OR NULLIF(btrim(g.unit_snapshot,' '),'') IS NULL
                  OR {Different("g.unit_snapshot", "l.unit_snapshot")}
                  OR g.demand_qty<>l.qty
                  OR COALESCE(g.required_date,TIMESTAMP '0001-01-01')
                     <>COALESCE(l.expected_delivery_date,h.delivery_date,TIMESTAMP '0001-01-01') THEN FALSE
                WHEN g.source_version IS NULL OR g.source_version<=0 OR g.source_version>h.version
                  OR g.demand_basis_fingerprint IS NULL
                  OR {InvalidFingerprint}
                  OR history.invalid_history_count>0 THEN FALSE ELSE TRUE END AS is_verifiable
        ) verified
        CROSS JOIN LATERAL (
            SELECT CASE WHEN verified.is_verifiable=FALSE THEN NULL::numeric
                        WHEN h.confirmation_status=@Pending THEN 0::numeric(18,4)
                        ELSE packing.delivered_qty::numeric(18,4) END AS shipped_qty
        ) delivery
        CROSS JOIN LATERAL (
            SELECT CASE
                WHEN verified.is_verifiable=FALSE AND h.confirmation_status=@Pending THEN '待确认仍有关联来源'
                WHEN verified.is_verifiable=FALSE AND g.uid IS NULL THEN '来源需求缺失'
                WHEN verified.is_verifiable=FALSE AND g.candidate_count<>1 THEN '来源需求重复'
                WHEN verified.is_verifiable=FALSE AND g.lifecycle_status<>@DemandActive
                     AND (h.document_status<>@OrderClosed OR g.lifecycle_status<>@DemandVoided) THEN '来源状态不一致'
                WHEN verified.is_verifiable=FALSE AND (g.source_version IS NULL OR g.source_version<=0
                    OR g.source_version>h.version OR g.demand_basis_fingerprint IS NULL
                    OR {InvalidFingerprint}) THEN '旧来源证据缺失'
                WHEN verified.is_verifiable=FALSE AND history.invalid_history_count>0 THEN '装箱过账历史异常'
                WHEN verified.is_verifiable=FALSE THEN '来源身份或单位不一致'
                WHEN delivery.shipped_qty>l.qty THEN '超发需核实' ELSE NULL END AS review_reason
        ) reason
        """;
}
