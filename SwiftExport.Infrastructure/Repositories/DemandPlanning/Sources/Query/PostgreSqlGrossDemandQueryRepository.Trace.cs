using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

/// <summary>以阶段独立SQL读取来源链路，保留历史与未知数量；只借用外层连接和可空事务。</summary>
public sealed partial class PostgreSqlGrossDemandQueryRepository
{
    /// <summary>读取指定来源的物料、采购和交付证据；取消及数据库错误原样传播，不建立或结束事务。</summary>
    public async Task<GrossDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var parameters = new { Uid = uid };
        var transaction = context.Session.Transaction;
        // 历史及软删除Link仍定位相关正式入库头；scope只物化一次，不收紧到当前活动资格。
        var scope = (await context.Session.Connection.QueryAsync<Guid>(new CommandDefinition(
            ScopeSql, parameters, transaction, cancellationToken: cancellationToken))).AsList();
        await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(context.Session, scope, cancellationToken);
        var header = await context.Session.Connection.QuerySingleOrDefaultAsync<TraceHeader>(new CommandDefinition(
            HeaderSql, parameters, transaction, cancellationToken: cancellationToken));
        // 源读取头会先推进第二结果；PG完整缓冲物料后才空头返回，避免未读reader取消外层事务。
        var materials = (await context.Session.Connection.QueryAsync<GrossDemandTraceMaterialDto>(new CommandDefinition(
            MaterialsSql, parameters, transaction, cancellationToken: cancellationToken))).AsList();
        if (header is null) return null;
        var detail = new GrossDemandTraceDetailDto
        {
            GrossDemandId = header.GrossDemandId,
            SourceType = header.SourceType,
            SourceUid = header.SourceUid,
            SourceNo = header.SourceNo,
            SourceLineNumber = header.SourceLineNumber,
            CustomerName = header.CustomerName,
            Sku = header.Sku,
            CustomerSku = header.CustomerSku,
            Unit = header.Unit,
            DemandQty = header.DemandQty,
            RequiredDate = header.RequiredDate,
            LifecycleStatus = header.LifecycleStatus,
            Materials = materials,
            Purchases = (await context.Session.Connection.QueryAsync<GrossDemandTracePurchaseDto>(new CommandDefinition(PurchasesSql, parameters, transaction, cancellationToken: cancellationToken))).AsList(),
            Deliveries = (await context.Session.Connection.QueryAsync<GrossDemandTraceDeliveryDto>(new CommandDefinition(DeliveriesSql, parameters, transaction, cancellationToken: cancellationToken))).AsList()
        };
        var invalidPackingHistoryCount = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(InvalidPackingHistorySql, parameters, transaction, cancellationToken: cancellationToken));
        // 来源身份理由独立展示；只有客户、单位及装箱历史理由阻断确定交付量。
        var sourceReviewReason = header.SourceUid == Guid.Empty || header.SourceLineId == Guid.Empty
            || string.IsNullOrWhiteSpace(header.SourceNo)
            || header.SourceType is not ("MD" or "SO" or "CR")
            || (header.SourceType == "MD" ? header.SourceVersion is not null : header.SourceVersion is null or <= 0)
            || header.DemandBasisFingerprint is not { Length: 64 }
            || header.DemandBasisFingerprint.Any(ch => !((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'F')))
            ? "来源身份、版本或需求指纹需核实" : string.Empty;
        var deliveryReviewReason = header.CustomerId is null ? "来源客户缺失" :
            string.IsNullOrWhiteSpace(header.Unit) ? "来源单位缺失" :
            invalidPackingHistoryCount>0 || detail.Deliveries.Any(row => !string.IsNullOrEmpty(row.ReviewReason))
                ? "装箱历史客户、来源、数量或单位需核实" : string.Empty;
        detail.ReviewReason = string.Join("；", new[] { sourceReviewReason, deliveryReviewReason }
            .Where(reason => !string.IsNullOrEmpty(reason)));
        detail.DeliveredQty = string.IsNullOrEmpty(deliveryReviewReason)
            ? detail.Deliveries.Where(row => row.CountsTowardDelivery).Sum(row => row.Qty)
            : null;
        return detail;
    }

    // 只读scope替代源临时表，保留全部PO历史关联，不创建会话资源。
    private const string ScopeSql = """
        SELECT DISTINCT link.target_line_id
        FROM public.net_demand net
        JOIN public.demand_execution_link link ON link.net_demand_id=net.uid
        WHERE net.gross_demand_id=@Uid AND rtrim(link.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
        """;
    // 此阶段独立读取；显式DTO别名不依赖全局Dapper名称映射。
    private const string HeaderSql = """
        SELECT g.uid AS "GrossDemandId",
            g.source_type AS "SourceType",
            g.source_uid AS "SourceUid",
            g.source_line_id AS "SourceLineId",
            g.source_no AS "SourceNo",
            g.source_line_number AS "SourceLineNumber",
            COALESCE(c.customer_name,'') AS "CustomerName",
            g.sku_snapshot AS "Sku",
            COALESCE(g.customer_sku_snapshot,'') AS "CustomerSku",
            COALESCE(g.unit_snapshot,'') AS "Unit",
            g.demand_qty AS "DemandQty",
            g.required_date AS "RequiredDate",
            g.lifecycle_status AS "LifecycleStatus",
            g.customer_id AS "CustomerId",
            g.source_version AS "SourceVersion",
            g.demand_basis_fingerprint AS "DemandBasisFingerprint"
        FROM public.gross_demand g
        LEFT JOIN public.customer c ON c.uid=g.customer_id AND c.is_deleted=FALSE
        WHERE g.uid=@Uid AND g.is_deleted=FALSE
        """;

    // 此阶段独立读取；显式DTO别名不依赖全局Dapper名称映射。
    private const string MaterialsSql = """
        -- 拆解修订历史均展示；只有来源版本、指纹可证明的活动行才能汇总当前采购贡献。
        SELECT n.uid AS "NetDemandId",
            n.decomposition_revision AS "DecompositionRevision",
            n.line_number AS "LineNumber",
            n.lifecycle_status AS "LifecycleStatus",
            n.sku_snapshot AS "Sku",
            COALESCE(n.material_type_snapshot,'') AS "MaterialType",
            n.net_qty AS "NetQty",
            CASE WHEN n.lifecycle_status=0 AND n.is_deleted=FALSE
                         AND n.source_demand_version_snapshot=g.version
                         AND NULLIF(rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci,'') IS NOT NULL
                         AND rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci=rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
                         AND g.customer_id IS NOT NULL AND customerMismatch.has_mismatch IS NULL
                         AND COALESCE(purchase.invalid_count,0)=0
                         AND ((rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='MD' AND g.source_version IS NULL AND n.source_version_snapshot IS NULL)
                              OR (g.source_version IS NOT NULL AND n.source_version_snapshot=g.source_version))
                    THEN COALESCE(purchase.linked_qty,0) ELSE NULL END AS "ActivePurchaseQty",
            CASE WHEN n.lifecycle_status<>0 THEN '历史拆解修订'
                    WHEN n.is_deleted<>FALSE THEN '已删除物料行'
                    WHEN g.customer_id IS NULL OR customerMismatch.has_mismatch IS NOT NULL
                      OR COALESCE(purchase.invalid_count,0)>0 THEN '活动采购 Link 目标、客户或单位需核实'
                    WHEN n.source_demand_version_snapshot IS NULL OR n.source_demand_version_snapshot<>g.version
                      OR NULLIF(rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                      OR n.source_demand_basis_fingerprint_snapshot IS NULL
                      OR rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci<>rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
                      OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci<>'MD' AND (g.source_version IS NULL OR n.source_version_snapshot IS NULL OR n.source_version_snapshot<>g.source_version))
                      OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='MD' AND n.source_version_snapshot IS NOT NULL)
                    THEN '来源版本或需求指纹需核实'
                    ELSE '' END AS "ReviewReason"
        FROM public.net_demand n
        JOIN public.gross_demand g ON g.uid=n.gross_demand_id
        LEFT JOIN public.product p ON p.uid=n.product_id AND p.is_deleted=FALSE
        LEFT JOIN LATERAL (
            SELECT SUM(CASE WHEN pol.uid IS NOT NULL AND poh.uid IS NOT NULL
                                   AND NULLIF(btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NOT NULL
                                   AND btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci=NULLIF(btrim(materialUnit.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'')
                            THEN l.allocated_qty-l.released_qty ELSE 0 END) AS linked_qty,
                   SUM(CASE WHEN pol.uid IS NULL OR poh.uid IS NULL
                                 OR poh.customer_id IS NULL
                                 OR NULLIF(btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                                 OR btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>NULLIF(btrim(materialUnit.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'')
                                 OR NULLIF(btrim(materialUnit.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                            THEN 1 ELSE 0 END) AS invalid_count
            FROM public.demand_execution_link l
            -- 单位进入聚合内的本地关系，避免 SQL Server 聚合表达式混用外部引用。
            JOIN public.net_demand materialUnit ON materialUnit.uid=l.net_demand_id
            LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
            LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE AND poh.lifecycle_status<>9
            WHERE l.net_demand_id=n.uid AND l.is_deleted=FALSE AND l.link_status=0
              AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND l.allocated_qty>l.released_qty
        ) purchase ON TRUE
        LEFT JOIN LATERAL (
            SELECT 1 AS has_mismatch
            FROM public.demand_execution_link l
            JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
            WHERE l.net_demand_id=n.uid AND l.is_deleted=FALSE AND l.link_status=0
              AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND l.allocated_qty>l.released_qty
              AND poh.customer_id<>g.customer_id
        LIMIT 1) customerMismatch ON TRUE
        WHERE n.gross_demand_id=@Uid
        ORDER BY n.decomposition_revision DESC,n.line_number,n.uid
        """;

    // 采购反馈按整行展示；同Sequence保留SQL Server Uid DESC字节优先级，不能使用普通PG UUID排序。
    private const string PurchasesSql = """
        -- Link 历史和采购目标同时展示；货好只属于整条采购行，不回分摊到来源需求。
        SELECT n.uid AS "NetDemandId",
            l.target_head_id AS "PurchaseOrderId",
            l.target_line_id AS "PurchaseLineId",
            CASE WHEN poh.uid IS NOT NULL AND pol.uid IS NOT NULL THEN TRUE ELSE FALSE END AS "HasPurchaseTarget",
            COALESCE(poh.order_no,l.target_no,'') AS "PurchaseOrderNo",
            COALESCE(pol.line_number,0) AS "PurchaseLineNumber",
            COALESCE(pol.sku,'') AS "Sku",
            COALESCE(pol.unit_snapshot,'') AS "Unit",
            COALESCE(poh.confirmation_status,0) AS "ConfirmationStatus",
            COALESCE(poh.lifecycle_status,9) AS "LifecycleStatus",
            l.allocated_qty AS "AllocatedQty",
            l.released_qty AS "ReleasedQty",
            l.allocated_qty-l.released_qty AS "LinkedQty",
            COALESCE(pol.quantity,0) AS "PurchaseLineQty",
            latest.ready_qty AS "PurchaseLineReadyQty",
            COALESCE(receipts.received_qty,0) AS "PurchaseLineReceivedQty",
            latest.expected_ready_date AS "ExpectedReadyDate",
            CASE WHEN EXISTS (
                   SELECT 1 FROM public.demand_execution_link other
                   JOIN public.net_demand otherNet ON otherNet.uid=other.net_demand_id
                   WHERE other.target_line_id=l.target_line_id AND other.net_demand_id<>n.uid
                     AND otherNet.gross_demand_id<>@Uid AND other.is_deleted=FALSE AND other.link_status=0
                     AND other.allocated_qty>other.released_qty
               ) THEN TRUE ELSE FALSE END AS "IsSharedPurchaseLine",
            CASE WHEN l.link_status<>0 OR l.is_deleted<>FALSE THEN '历史采购关联'
                    WHEN pol.uid IS NULL OR poh.uid IS NULL OR poh.lifecycle_status=9 THEN '采购目标需核实'
                    WHEN poh.customer_id IS NULL OR g.customer_id IS NULL OR poh.customer_id<>g.customer_id THEN '客户归属需核实'
                    WHEN NULLIF(btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                      OR NULLIF(btrim(n.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                      OR btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>NULLIF(btrim(n.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'')
                    THEN '采购行与物料单位需核实'
                    WHEN n.lifecycle_status<>0 OR n.source_demand_version_snapshot IS NULL OR n.source_demand_version_snapshot<>g.version
                      OR NULLIF(rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                      OR n.source_demand_basis_fingerprint_snapshot IS NULL
                      OR rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci<>rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
                      OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci<>'MD' AND (g.source_version IS NULL OR n.source_version_snapshot IS NULL OR n.source_version_snapshot<>g.source_version))
                      OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='MD' AND n.source_version_snapshot IS NOT NULL)
                    THEN '来源修订需核实' ELSE '' END AS "ReviewReason"
        FROM public.net_demand n
        JOIN public.gross_demand g ON g.uid=n.gross_demand_id
        LEFT JOIN public.product p ON p.uid=n.product_id AND p.is_deleted=FALSE
        JOIN public.demand_execution_link l ON l.net_demand_id=n.uid
        LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
        LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
        LEFT JOIN LATERAL (
            SELECT report.ready_qty,report.expected_ready_date
            FROM public.supplier_progress_report report
            WHERE report.purchase_order_line_id=pol.uid AND report.is_deleted=FALSE
            ORDER BY report.sequence DESC,ARRAY[get_byte(uuid_send(report.uid),10),get_byte(uuid_send(report.uid),11),get_byte(uuid_send(report.uid),12),get_byte(uuid_send(report.uid),13),get_byte(uuid_send(report.uid),14),get_byte(uuid_send(report.uid),15),get_byte(uuid_send(report.uid),8),get_byte(uuid_send(report.uid),9),get_byte(uuid_send(report.uid),7),get_byte(uuid_send(report.uid),6),get_byte(uuid_send(report.uid),5),get_byte(uuid_send(report.uid),4),get_byte(uuid_send(report.uid),3),get_byte(uuid_send(report.uid),2),get_byte(uuid_send(report.uid),1),get_byte(uuid_send(report.uid),0)] DESC
        LIMIT 1) latest ON TRUE
        LEFT JOIN LATERAL (
            SELECT SUM(receiptLine.quantity) AS received_qty
            FROM public.inbound_document_line receiptLine
            JOIN public.inbound_document receiptHead ON receiptHead.uid=receiptLine.inbound_document_id
                AND receiptHead.is_deleted=FALSE AND receiptHead.document_type=10 AND receiptHead.posting_status=1
            WHERE receiptLine.source_line_id=pol.uid AND receiptLine.is_deleted=FALSE
        ) receipts ON TRUE
        WHERE n.gross_demand_id=@Uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
        ORDER BY n.decomposition_revision DESC,poh.order_no NULLS FIRST,pol.line_number NULLS FIRST,l.uid
        """;

    // 此阶段独立读取；显式DTO别名不依赖全局Dapper名称映射。
    private const string DeliveriesSql = """
        SELECT ph.uid AS "PackingListId",
            pl.uid AS "PackingListLineId",
            ph.packing_list_no AS "PackingListNo",
            pl.line_number AS "LineNumber",
            pl.qty AS "Qty",
            COALESCE(pl.unit_snapshot,'') AS "Unit",
            CASE WHEN ph.posting_status=1 THEN TRUE ELSE FALSE END AS "CountsTowardDelivery",
            ph.posted_date AS "PostedDate",
            ph.reversed_date AS "ReversedDate",
            CASE WHEN ph.customer_id IS NULL OR ph.customer_id<>g.customer_id OR g.customer_id IS NULL THEN '交付客户需核实'
                     WHEN rtrim(COALESCE(pl.source_type_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci OR rtrim(COALESCE(pl.source_document_no_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.source_no,' ') COLLATE public.erp_dictionary_ci
                       OR COALESCE(pl.source_line_number_snapshot,-1)<>COALESCE(g.source_line_number,-1)
                       OR rtrim(COALESCE(pl.sku_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.sku_snapshot,' ') COLLATE public.erp_dictionary_ci OR pl.is_non_standard_snapshot<>g.is_non_standard
                       OR COALESCE(pl.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                          <>COALESCE(g.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                       OR pl.qty<=0 OR (ph.posting_status IN (1,8) AND ph.posted_date IS NULL)
                       OR (ph.posting_status=8 AND ph.reversed_date IS NULL)
                       THEN '装箱来源或正式历史需核实'
                    WHEN NULLIF(btrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                      OR NULLIF(btrim(g.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                      OR btrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(g.unit_snapshot,' ') COLLATE public.erp_dictionary_ci THEN '交付单位需核实'
                    ELSE '' END AS "ReviewReason"
        FROM public.packing_list_line pl
        JOIN public.packing_list_head ph ON ph.uid=pl.packing_list_head_id AND ph.is_deleted=FALSE
        JOIN public.gross_demand g ON g.uid=pl.gross_demand_id
        WHERE pl.gross_demand_id=@Uid AND pl.is_deleted=FALSE AND ph.posting_status IN (1,8)
        ORDER BY ph.posted_date NULLS FIRST,ph.uid,pl.line_number,pl.uid
        """;

    // 此阶段独立读取；显式DTO别名不依赖全局Dapper名称映射。
    private const string InvalidPackingHistorySql = """
        -- 被软删除的已过账行和无法定位单头的行不会出现在展示集合，仍必须阻断确定性已交量。
        SELECT COUNT(*)::integer FROM public.packing_list_line pl
        LEFT JOIN public.packing_list_head ph ON ph.uid=pl.packing_list_head_id
        JOIN public.gross_demand g ON g.uid=pl.gross_demand_id
        WHERE pl.gross_demand_id=@Uid AND (ph.uid IS NULL
            OR (pl.is_deleted=TRUE AND ph.posting_status IN (1,8))
            OR (pl.is_deleted=FALSE AND (ph.is_deleted=TRUE OR ph.posting_status NOT IN (0,1,8)
                OR ph.customer_id IS NULL OR ph.customer_id<>g.customer_id OR g.customer_id IS NULL
                OR rtrim(COALESCE(pl.source_type_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci OR rtrim(COALESCE(pl.source_document_no_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.source_no,' ') COLLATE public.erp_dictionary_ci
                OR COALESCE(pl.source_line_number_snapshot,-1)<>COALESCE(g.source_line_number,-1)
                OR rtrim(COALESCE(pl.sku_snapshot,''),' ') COLLATE public.erp_dictionary_ci<>rtrim(g.sku_snapshot,' ') COLLATE public.erp_dictionary_ci OR pl.is_non_standard_snapshot<>g.is_non_standard
                OR COALESCE(pl.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                   <>COALESCE(g.product_id,'00000000-0000-0000-0000-000000000000'::uuid)
                OR pl.qty<=0 OR NULLIF(btrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci,'') IS NULL
                OR rtrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>rtrim(g.unit_snapshot,' ') COLLATE public.erp_dictionary_ci
                OR (ph.posting_status IN (1,8) AND ph.posted_date IS NULL)
                OR (ph.posting_status=8 AND ph.reversed_date IS NULL))))
        """;

    /// <summary>仅用于接收追踪头查询的客户身份，不作为对外契约。</summary>
    private sealed class TraceHeader
    {
        public Guid GrossDemandId { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public Guid SourceUid { get; set; }
        public Guid SourceLineId { get; set; }
        public int? SourceVersion { get; set; }
        public string? DemandBasisFingerprint { get; set; }
        public string SourceNo { get; set; } = string.Empty;
        public int? SourceLineNumber { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string CustomerSku { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal DemandQty { get; set; }
        public DateTime? RequiredDate { get; set; }
        public GrossDemandLifecycleStatusEnum LifecycleStatus { get; set; }
    }

}
