using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;

/// <summary>按冻结读取阶段借用调用者会话，分别保留起始反向证明范围和稍后收货覆盖投影。</summary>
public sealed partial class PostgreSqlMaterialDemandQueryRepository
{
    /// <summary>读取物料来源、全部未删修订与历史采购关系；不拥有连接或事务，未知量不回落为零。</summary>
    public async Task<MaterialDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new { Uid = uid };
        var scope = (await context.Session.Connection.QueryAsync<Guid>(Command(
            "SELECT DISTINCT target_line_id FROM public.demand_execution_link WHERE net_demand_id=@Uid AND rtrim(target_type,' ') COLLATE public.erp_dictionary_ci='PO';",
            parameters, cancellationToken))).ToArray();
        await PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync(context.Session, scope, cancellationToken);
        var header = await context.Session.Connection.QuerySingleOrDefaultAsync<TraceHeader>(Command(TraceHeaderSql, parameters, cancellationToken));
        // 先缓冲第二阶段再判断空头，避免关闭未读reader时内部取消损伤调用者事务。
        var tree = (await context.Session.Connection.QueryAsync<MaterialDemandTraceTreeItemDto>(Command(TraceTreeSql, parameters, cancellationToken))).ToList();
        if (header is null) return null;
        var frozen = (await context.Session.Connection.QueryAsync<ReceiptCoverageRow>(Command(TraceCoverageSql, parameters, cancellationToken))).ToArray();
        var purchaseParameters = new FrozenCoverageParameters(uid, frozen);
        var purchases = (await context.Session.Connection.QueryAsync<MaterialDemandTracePurchaseDto>(Command(TracePurchaseSql, purchaseParameters, cancellationToken))).ToList();
        var reason = GetSourceReviewReason(header);
        if (string.IsNullOrEmpty(reason) && purchases.Any(row => row.IsActiveLink && !string.IsNullOrEmpty(row.ReviewReason)))
            reason = "采购目标、客户、单位或来源修订需核实";
        var associated = header.LifecycleStatus == NetDemandLifecycleStatusEnum.Active && string.IsNullOrEmpty(reason)
            ? purchases.Where(row => row.IsActiveLink).Sum(row => row.LinkedQty) : (decimal?)null;
        if (associated > header.NetQty) reason = "采购承接超量需核实";
        var coverage = associated.HasValue && string.IsNullOrEmpty(reason)
            ? purchases.Where(row => row.IsActiveLink).Sum(row => row.CurrentReceiptCoverageQty ?? 0m)
            : (decimal?)null;
        return new MaterialDemandTraceDetailDto
        {
            NetDemandId = header.NetDemandId, GrossDemandId = header.GrossDemandId,
            SourceType = header.SourceType, SourceNo = header.SourceNo,
            SourceLineNumber = header.SourceLineNumber, DecompositionRevision = header.DecompositionRevision,
            LineNumber = header.LineNumber, LifecycleStatus = header.LifecycleStatus,
            Sku = header.Sku, Unit = header.Unit, NetQty = header.NetQty, RequiredDate = header.RequiredDate,
            RolledBackAt = header.RolledBackAt, RolledBackBy = header.RolledBackBy,
            RolledBackReason = header.RolledBackReason, ParentNetDemandId = header.ParentNetDemandId,
            RootNetDemandId = header.RootNetDemandId, AssociatedPurchaseQty = associated,
            CurrentReceiptCoverageQty = coverage, ReviewReason = reason, TreeItems = tree, Purchases = purchases
        };
    }

    /// <summary>老来源缺少版本、指纹或单位时不借物料行推断可信身份。</summary>
    private static string GetSourceReviewReason(TraceHeader header)
    {
        if (header.LifecycleStatus != NetDemandLifecycleStatusEnum.Active) return "历史拆解修订";
        if (header.SourceLifecycleStatus != GrossDemandLifecycleStatusEnum.Active || header.CustomerId is null)
            return "来源状态或客户需核实";
        if (header.SourceDemandVersionSnapshot is null
            || header.SourceDemandVersionSnapshot != header.SourceDemandVersion
            || string.IsNullOrWhiteSpace(header.DemandBasisFingerprint)
            || header.SourceDemandBasisFingerprintSnapshot != header.DemandBasisFingerprint
            || (header.SourceType == "MD" && (header.SourceVersion is not null || header.SourceVersionSnapshot is not null))
            || (header.SourceType != "MD" && (header.SourceVersion is null
                || header.SourceVersionSnapshot != header.SourceVersion)))
            return "来源版本或需求指纹需核实";
        if (string.IsNullOrWhiteSpace(header.Unit)) return "物料单位需核实";
        return header.NetQty <= 0 ? "物料需求数量需核实" : string.Empty;
    }

    /// <summary>Repository 内部承载来源版本和指纹证据，不暴露给前端。</summary>
    private sealed class TraceHeader : MaterialDemandTraceDetailDto
    {
        public GrossDemandLifecycleStatusEnum SourceLifecycleStatus { get; set; }
        public Guid? CustomerId { get; set; }
        public int SourceDemandVersion { get; set; }
        public int? SourceDemandVersionSnapshot { get; set; }
        public int? SourceVersion { get; set; }
        public int? SourceVersionSnapshot { get; set; }
        public string? DemandBasisFingerprint { get; set; }
        public string? SourceDemandBasisFingerprintSnapshot { get; set; }
    }

    /// <summary>一次覆盖SQL的局部结果，允许未知覆盖量；不持久化历史归属。</summary>
    private sealed class ReceiptCoverageRow
    {
        public Guid LinkId { get; set; }
        public Guid TargetLineId { get; set; }
        public decimal? CurrentReceiptCoverageQty { get; set; }
    }

    /// <summary>按相同顺序绑定等长数组，保留空集合和decimal空值，避免Dapper展开参数。</summary>
    private sealed class FrozenCoverageParameters(Guid uid, ReceiptCoverageRow[] rows) : SqlMapper.IDynamicParameters
    {
        /// <summary>为当前采购阶段绑定冻结数组，不读数据库或改变事务。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            command.Parameters.Add(new NpgsqlParameter("Uid", NpgsqlDbType.Uuid) { Value = uid });
            command.Parameters.Add(new NpgsqlParameter("CoverageLinkIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = rows.Select(r => r.LinkId).ToArray() });
            command.Parameters.Add(new NpgsqlParameter("CoverageTargetLineIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = rows.Select(r => r.TargetLineId).ToArray() });
            command.Parameters.Add(new NpgsqlParameter("CoverageQuantities", NpgsqlDbType.Array | NpgsqlDbType.Numeric) { Value = rows.Select(r => r.CurrentReceiptCoverageQty).ToArray() });
        }
    }

    // 独立只读阶段：SQL保留原资格、空值和汇总边界，不复用起始scope替代当前Link范围。
    private const string TraceHeaderSql = """
        SELECT n.uid AS "NetDemandId",
        n.gross_demand_id AS "GrossDemandId",
        g.source_type AS "SourceType",
        g.source_no AS "SourceNo",
        g.source_line_number AS "SourceLineNumber",
        n.decomposition_revision AS "DecompositionRevision",
        n.line_number AS "LineNumber",
        n.lifecycle_status AS "LifecycleStatus",
        n.sku_snapshot AS "Sku",
        NULLIF(btrim(n.unit_snapshot,' '),'') AS "Unit",
        n.net_qty AS "NetQty",
        n.required_date AS "RequiredDate",
        n.rolled_back_at AS "RolledBackAt",
        n.rolled_back_by AS "RolledBackBy",
        n.rolled_back_reason AS "RolledBackReason",
        n.parent_net_demand_id AS "ParentNetDemandId",
        n.root_net_demand_id AS "RootNetDemandId",
        g.lifecycle_status AS "SourceLifecycleStatus",
        g.customer_id AS "CustomerId",
        g.version AS "SourceDemandVersion",
        g.source_version AS "SourceVersion",
        g.demand_basis_fingerprint AS "DemandBasisFingerprint",
        n.source_demand_version_snapshot AS "SourceDemandVersionSnapshot",
        n.source_version_snapshot AS "SourceVersionSnapshot",
        n.source_demand_basis_fingerprint_snapshot AS "SourceDemandBasisFingerprintSnapshot"
        FROM public.net_demand n
        JOIN public.gross_demand g ON g.uid=n.gross_demand_id
        LEFT JOIN public.product p ON p.uid=n.product_id AND p.is_deleted=FALSE
        WHERE n.uid=@Uid AND n.is_deleted=FALSE;
        """;

    // 独立只读阶段：SQL保留原资格、空值和汇总边界，不复用起始scope替代当前Link范围。
    private const string TraceTreeSql = """
        SELECT sibling.uid AS "NetDemandId",
        sibling.parent_net_demand_id AS "ParentNetDemandId",
        sibling.decomposition_revision AS "DecompositionRevision",
        sibling.line_number AS "LineNumber",
        sibling.lifecycle_status AS "LifecycleStatus",
        sibling.sku_snapshot AS "Sku",
        sibling.net_qty AS "NetQty"
        FROM public.net_demand sibling
        JOIN public.net_demand selected ON selected.gross_demand_id=sibling.gross_demand_id
        WHERE selected.uid=@Uid AND sibling.is_deleted=FALSE
        ORDER BY sibling.decomposition_revision DESC,sibling.line_number,sibling.uid;
        """;

    // 独立只读阶段：SQL保留原资格、空值和汇总边界，不复用起始scope替代当前Link范围。
    private const string TraceCoverageSql = """
        WITH eligible AS
        (
        SELECT l.uid AS link_id,
        l.target_line_id AS target_line_id,
        l.allocated_qty-l.released_qty AS linked_qty,
        l.demand_required_date AS demand_required_date,
        g.source_line_number AS source_line_number,
        pol.quantity AS purchase_line_qty,
        pol.unit_snapshot AS purchase_line_unit,
        CASE WHEN n.lifecycle_status=0 AND n.is_deleted=FALSE AND g.lifecycle_status=0 AND g.is_deleted=FALSE
        AND g.customer_id IS NOT NULL AND poh.customer_id=g.customer_id
        AND n.source_demand_version_snapshot=g.version
        AND NULLIF(rtrim(g.demand_basis_fingerprint,' '),'') IS NOT NULL
        AND rtrim(n.source_demand_basis_fingerprint_snapshot,' ') COLLATE public.erp_dictionary_ci=rtrim(g.demand_basis_fingerprint,' ') COLLATE public.erp_dictionary_ci
        AND ((rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci='MD' AND g.source_version IS NULL AND n.source_version_snapshot IS NULL)
        OR (rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci<>'MD' AND g.source_version IS NOT NULL
        AND n.source_version_snapshot=g.source_version))
        AND NULLIF(btrim(pol.unit_snapshot,' '),'') IS NOT NULL
        AND btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci=NULLIF(btrim(n.unit_snapshot,' '),'') COLLATE public.erp_dictionary_ci
        AND poh.lifecycle_status=0 AND poh.is_deleted=FALSE AND pol.is_deleted=FALSE
        THEN 1 ELSE 0 END AS is_valid
        FROM public.demand_execution_link l
        JOIN public.net_demand n ON n.uid=l.net_demand_id
        JOIN public.gross_demand g ON g.uid=n.gross_demand_id
        LEFT JOIN public.product p ON p.uid=n.product_id AND p.is_deleted=FALSE
        JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id
        JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id
        WHERE rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND l.link_status=0 AND l.is_deleted=FALSE
        AND l.allocated_qty>l.released_qty
        AND l.target_line_id IN (SELECT target_line_id FROM public.demand_execution_link WHERE net_demand_id=@Uid AND rtrim(target_type,' ') COLLATE public.erp_dictionary_ci='PO')
        ), ranked AS
        (
        SELECT e.*,
        SUM(e.linked_qty) OVER (PARTITION BY e.target_line_id) AS total_linked_qty,
        SUM(CASE WHEN e.is_valid=0 THEN 1 ELSE 0 END) OVER (PARTITION BY e.target_line_id) AS invalid_count,
        SUM(e.linked_qty) OVER (PARTITION BY e.target_line_id
        ORDER BY CASE WHEN e.demand_required_date IS NULL THEN 1 ELSE 0 END,e.demand_required_date NULLS LAST,
        CASE WHEN e.source_line_number IS NULL THEN 1 ELSE 0 END,e.source_line_number NULLS LAST,ARRAY[get_byte(uuid_send(e.link_id),10),get_byte(uuid_send(e.link_id),11),get_byte(uuid_send(e.link_id),12),get_byte(uuid_send(e.link_id),13),get_byte(uuid_send(e.link_id),14),get_byte(uuid_send(e.link_id),15),get_byte(uuid_send(e.link_id),8),get_byte(uuid_send(e.link_id),9),get_byte(uuid_send(e.link_id),7),get_byte(uuid_send(e.link_id),6),get_byte(uuid_send(e.link_id),5),get_byte(uuid_send(e.link_id),4),get_byte(uuid_send(e.link_id),3),get_byte(uuid_send(e.link_id),2),get_byte(uuid_send(e.link_id),1),get_byte(uuid_send(e.link_id),0)]
        ROWS UNBOUNDED PRECEDING)-e.linked_qty AS earlier_linked_qty
        FROM eligible e
        )
        SELECT r.link_id AS "LinkId",r.target_line_id AS "TargetLineId",
        CASE WHEN r.invalid_count=0 AND r.total_linked_qty<=r.purchase_line_qty
        AND r.purchase_line_qty>=0 AND COALESCE(receipts.received_qty,0)>=0
        AND invalidReceipts.invalid_receipt_count=0
        THEN CASE WHEN COALESCE(receipts.received_qty,0)<=r.earlier_linked_qty THEN 0
        WHEN COALESCE(receipts.received_qty,0)>=r.earlier_linked_qty+r.linked_qty THEN r.linked_qty
        ELSE COALESCE(receipts.received_qty,0)-r.earlier_linked_qty END END AS "CurrentReceiptCoverageQty"
        FROM ranked r
        LEFT JOIN LATERAL (
        SELECT SUM(receiptLine.quantity) AS received_qty
        FROM public.inbound_document_line receiptLine
        JOIN public.inbound_document receiptHead ON receiptHead.uid=receiptLine.inbound_document_id
        AND receiptHead.is_deleted=FALSE AND receiptHead.document_type=10 AND receiptHead.posting_status=1
        WHERE receiptLine.source_line_id=r.target_line_id AND receiptLine.is_deleted=FALSE
        ) receipts ON TRUE
        LEFT JOIN LATERAL (
        SELECT COUNT(*) AS invalid_receipt_count
        FROM public.inbound_document_line receiptLine
        JOIN public.inbound_document receiptHead ON receiptHead.uid=receiptLine.inbound_document_id
        AND receiptHead.is_deleted=FALSE AND receiptHead.document_type=10 AND receiptHead.posting_status=1
        WHERE receiptLine.source_line_id=r.target_line_id AND receiptLine.is_deleted=FALSE
        AND (NULLIF(btrim(receiptLine.unit_snapshot,' '),'') IS NULL
        OR btrim(receiptLine.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(r.purchase_line_unit,' ') COLLATE public.erp_dictionary_ci)
        ) invalidReceipts ON TRUE;
        """;

    // 独立只读阶段：SQL保留原资格、空值和汇总边界，不复用起始scope替代当前Link范围。
    private const string TracePurchaseSql = """
        WITH coverage AS (SELECT frozen.link_id,frozen.target_line_id,frozen.current_receipt_coverage_qty FROM unnest(@CoverageLinkIds::uuid[], @CoverageTargetLineIds::uuid[], @CoverageQuantities::numeric[]) AS frozen(link_id,target_line_id,current_receipt_coverage_qty))
        SELECT l.uid AS "LinkId",l.target_head_id AS "PurchaseOrderId",l.target_line_id AS "PurchaseLineId",
        (CASE WHEN poh.uid IS NOT NULL AND pol.uid IS NOT NULL THEN TRUE ELSE FALSE END) AS "HasPurchaseTarget",
        COALESCE(poh.order_no,l.target_no,'') AS "PurchaseOrderNo",COALESCE(pol.line_number,0) AS "PurchaseLineNumber",
        COALESCE(supplier.supplier_name,'') AS "ActualSupplierName",
        COALESCE(poh.confirmation_status,0) AS "ConfirmationStatus",
        COALESCE(poh.lifecycle_status,9) AS "LifecycleStatus",
        (CASE WHEN l.link_status=0 AND l.is_deleted=FALSE AND l.allocated_qty>l.released_qty
        AND n.lifecycle_status=0 THEN TRUE ELSE FALSE END) AS "IsActiveLink",
        l.allocated_qty AS "AllocatedQty",l.released_qty AS "ReleasedQty",l.allocated_qty-l.released_qty AS "LinkedQty",
        COALESCE(pol.quantity,0) AS "PurchaseLineQty",COALESCE(pol.unit_snapshot,'') AS "Unit",
        latest.ready_qty AS "PurchaseLineReadyQty",latest.expected_ready_date AS "ExpectedReadyDate",
        COALESCE(receipts.received_qty,0) AS "PurchaseLineReceivedQty",
        CASE WHEN l.link_status=0 AND l.is_deleted=FALSE AND n.lifecycle_status=0
        THEN coverage.current_receipt_coverage_qty END AS "CurrentReceiptCoverageQty",
        CASE WHEN COALESCE(totals.invalid_count,0)=0 AND invalidReceipts.invalid_receipt_count=0 THEN
        CASE WHEN COALESCE(receipts.received_qty,0)>COALESCE(totals.covered_qty,0)
        THEN COALESCE(receipts.received_qty,0)-COALESCE(totals.covered_qty,0) ELSE 0 END END
        AS "ReceiptOutsideCurrentCoverageQty",
        COALESCE(history.historical_linked_qty,0) AS "HistoricalLinkedQty",
        (CASE WHEN EXISTS (SELECT 1 FROM public.demand_execution_link other
        WHERE other.target_line_id=l.target_line_id AND other.net_demand_id<>@Uid
        AND rtrim(other.target_type,' ') COLLATE public.erp_dictionary_ci='PO' AND other.link_status=0 AND other.is_deleted=FALSE
        AND other.allocated_qty>other.released_qty) THEN TRUE ELSE FALSE END) AS "IsSharedPurchaseLine",
        CASE WHEN l.link_status<>0 OR l.is_deleted<>FALSE OR n.lifecycle_status<>0 THEN '历史采购关联'
        WHEN poh.uid IS NULL OR pol.uid IS NULL OR poh.lifecycle_status<>0 THEN '采购目标需核实'
        WHEN poh.customer_id IS NULL OR g.customer_id IS NULL OR poh.customer_id<>g.customer_id THEN '客户归属需核实'
        WHEN NULLIF(btrim(n.unit_snapshot,' '),'') IS NULL
        OR NULLIF(btrim(pol.unit_snapshot,' '),'') IS NULL
        OR NULLIF(btrim(pol.unit_snapshot,' '),'') COLLATE public.erp_dictionary_ci<>NULLIF(btrim(n.unit_snapshot,' '),'') COLLATE public.erp_dictionary_ci
        THEN '采购单位需核实'
        WHEN invalidReceipts.invalid_receipt_count>0 THEN '入库单位需核实'
        WHEN coverage.current_receipt_coverage_qty IS NULL THEN '来源修订或采购行贡献需核实'
        ELSE '' END AS "ReviewReason"
        FROM public.demand_execution_link l
        JOIN public.net_demand n ON n.uid=l.net_demand_id
        JOIN public.gross_demand g ON g.uid=n.gross_demand_id
        LEFT JOIN public.product p ON p.uid=n.product_id AND p.is_deleted=FALSE
        LEFT JOIN public.purchase_order_head poh ON poh.uid=l.target_head_id AND poh.is_deleted=FALSE
        LEFT JOIN public.purchase_order_line pol ON pol.uid=l.target_line_id AND pol.purchase_order_id=l.target_head_id AND pol.is_deleted=FALSE
        LEFT JOIN public.supplier supplier ON supplier.uid=poh.supplier_id
        LEFT JOIN coverage coverage ON coverage.link_id=l.uid
        LEFT JOIN LATERAL (SELECT SUM(cover2.current_receipt_coverage_qty) AS covered_qty,
        SUM(CASE WHEN cover2.current_receipt_coverage_qty IS NULL THEN 1 ELSE 0 END) AS invalid_count
        FROM coverage cover2 WHERE cover2.target_line_id=l.target_line_id) totals ON TRUE
        LEFT JOIN LATERAL (SELECT SUM(old.allocated_qty-old.released_qty) AS historical_linked_qty
        FROM public.demand_execution_link old
        JOIN public.net_demand oldNet ON oldNet.uid=old.net_demand_id
        WHERE old.target_line_id=l.target_line_id AND rtrim(old.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
        AND (old.link_status<>0 OR old.is_deleted<>FALSE OR oldNet.lifecycle_status<>0)) history ON TRUE
        LEFT JOIN LATERAL (SELECT report.ready_qty,report.expected_ready_date
        FROM public.supplier_progress_report report
        WHERE report.purchase_order_line_id=pol.uid AND report.is_deleted=FALSE
        ORDER BY report.sequence DESC,ARRAY[get_byte(uuid_send(report.uid),10),get_byte(uuid_send(report.uid),11),get_byte(uuid_send(report.uid),12),get_byte(uuid_send(report.uid),13),get_byte(uuid_send(report.uid),14),get_byte(uuid_send(report.uid),15),get_byte(uuid_send(report.uid),8),get_byte(uuid_send(report.uid),9),get_byte(uuid_send(report.uid),7),get_byte(uuid_send(report.uid),6),get_byte(uuid_send(report.uid),5),get_byte(uuid_send(report.uid),4),get_byte(uuid_send(report.uid),3),get_byte(uuid_send(report.uid),2),get_byte(uuid_send(report.uid),1),get_byte(uuid_send(report.uid),0)] DESC LIMIT 1) latest ON TRUE
        LEFT JOIN LATERAL (SELECT SUM(receiptLine.quantity) AS received_qty
        FROM public.inbound_document_line receiptLine
        JOIN public.inbound_document receiptHead ON receiptHead.uid=receiptLine.inbound_document_id
        AND receiptHead.is_deleted=FALSE AND receiptHead.document_type=10 AND receiptHead.posting_status=1
        WHERE receiptLine.source_line_id=pol.uid AND receiptLine.is_deleted=FALSE) receipts ON TRUE
        LEFT JOIN LATERAL (SELECT COUNT(*) AS invalid_receipt_count
        FROM public.inbound_document_line receiptLine
        JOIN public.inbound_document receiptHead ON receiptHead.uid=receiptLine.inbound_document_id
        AND receiptHead.is_deleted=FALSE AND receiptHead.document_type=10 AND receiptHead.posting_status=1
        WHERE receiptLine.source_line_id=pol.uid AND receiptLine.is_deleted=FALSE
        AND (NULLIF(btrim(receiptLine.unit_snapshot,' '),'') IS NULL
        OR btrim(receiptLine.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>btrim(pol.unit_snapshot,' ') COLLATE public.erp_dictionary_ci)) invalidReceipts ON TRUE
        WHERE l.net_demand_id=@Uid AND rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
        ORDER BY poh.order_no,pol.line_number,l.uid;
        """;
}
