using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

/// <summary>采购管理PG只读投影：核实全局历史后由一次SQL冻结商业事实；借用会话事务，不接运行DI。</summary>
public sealed class PostgreSqlPurchaseOrderManagerQueryRepository(RepositoryBaseContext context) : IPurchaseOrderManagerQueryRepository
{
    /// <summary>SQL完成商业计算、筛选及源排序，完整缓冲后仅计数和分页，不重读真实头。</summary>
    public async Task<PagedResponseBase<PurchaseOrderManagerListItemDto>> SearchAsync(PurchaseOrderManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureFormalReceiptHistoryIsTraceableAsync(cancellationToken);
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "China Standard Time").Date;
        var p = new ManagerParameters();
        p.Add("Keyword", ToLikePattern(request.Keyword), NpgsqlDbType.Text);
        p.Add("CustomerCode", string.IsNullOrWhiteSpace(request.CustomerCode) ? null : request.CustomerCode.Trim(), NpgsqlDbType.Text);
        p.Add("SupplierId", request.SupplierId, NpgsqlDbType.Uuid);
        p.Add("OnlyWithUnlinkedQuantity", request.OnlyWithUnlinkedQuantity, NpgsqlDbType.Boolean);
        p.Timestamp("OrderDateFrom", request.OrderDateFrom?.Date);
        p.Timestamp("OrderDateToExclusive", request.OrderDateTo.HasValue && request.OrderDateTo.Value.Date < DateTime.MaxValue.Date ? request.OrderDateTo.Value.Date.AddDays(1) : null);
        p.Statuses("ConfirmationStatuses", request.ConfirmationStatuses.Select(x => (int)x));
        p.Statuses("LifecycleStatuses", request.LifecycleStatuses.Select(x => (int)x));
        p.Statuses("ReadyStatuses", request.ReadyStatuses.Select(x => (int)x));
        p.Statuses("ReceiptStatuses", request.ReceiptStatuses.Select(x => (int)x));
        p.Add("DueView", (int)request.DueView, NpgsqlDbType.Integer);
        p.Timestamp("BusinessToday", today); p.Timestamp("DueSoonExclusive", today.AddDays(8));
        p.Timestamp("PlannedDeliveryDateFrom", request.PlannedDeliveryDateFrom); p.Timestamp("PlannedDeliveryDateTo", request.PlannedDeliveryDateTo);
        p.Timestamp("ExpectedCompletionDateFrom", request.ExpectedCompletionDateFrom); p.Timestamp("ExpectedCompletionDateTo", request.ExpectedCompletionDateTo);
        p.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt, NpgsqlDbType.Integer);
        p.Add("ActiveLifecycle", (int)PurchaseOrderLifecycleStatusEnum.Active, NpgsqlDbType.Integer);
        p.Add("PendingConfirmation", (int)ConfirmationStatusEnum.待确认, NpgsqlDbType.Integer);
        p.Add("Confirmed", (int)ConfirmationStatusEnum.已确认, NpgsqlDbType.Integer);
        var rows = (await context.Session.Connection.QueryAsync<PurchaseOrderManagerListItemDto>(Command(SearchSql, p, cancellationToken))).ToList();
        return new() { TotalCount = rows.Count, Items = rows.Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>从表直接保留采购行数量、未知反馈和当前图片，不采用头的跨单位遮罩。</summary>
    public async Task<IReadOnlyList<PurchaseOrderManagerLineDto>> GetLinesAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
    {
        await EnsureFormalReceiptHistoryIsTraceableAsync(cancellationToken);
        var p = new ManagerParameters(); p.Add("PurchaseOrderId", purchaseOrderId, NpgsqlDbType.Uuid);
        p.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt, NpgsqlDbType.Integer);
        p.Add("CustomerSuppliedMaterialType", MaterialTypesEnum.客供件, NpgsqlDbType.Text);
        return (await context.Session.Connection.QueryAsync<PurchaseOrderManagerLineDto>(Command(LinesSql, p, cancellationToken))).ToList();
    }

    /// <summary>先复用全局反向证明，再检查无法定位采购行的正式历史；存在但软删采购行仍可定位。</summary>
    private async Task EnsureFormalReceiptHistoryIsTraceableAsync(CancellationToken token)
    {
        await PostgreSqlInboundReceiptHistoryGuard.ValidateGlobalAsync(context.Session, token);
        var p = new ManagerParameters(); p.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt, NpgsqlDbType.Integer);
        var invalid = await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT EXISTS(SELECT 1 FROM public.inbound_document_line receipt_line
                JOIN public.inbound_document receipt_head ON receipt_head.uid=receipt_line.inbound_document_id
                LEFT JOIN public.purchase_order_line purchase_line ON purchase_line.uid=receipt_line.source_line_id
                WHERE receipt_head.document_type=@PurchaseReceipt AND receipt_head.posting_status IN(1,8)
                  AND receipt_head.is_deleted=FALSE AND receipt_line.is_deleted=FALSE
                  AND (receipt_line.source_line_id IS NULL OR purchase_line.uid IS NULL))
            """, p, token));
        if (invalid) throw new InvalidOperationException("存在无法定位采购行的正式采购入库历史，请先完成数据核实与迁移。");
    }

    /// <summary>只借用调用方事务及令牌，不新增事务、连接或锁。</summary>
    private CommandDefinition Command(string sql, ManagerParameters p, CancellationToken token) => new(sql, p, context.Session.Transaction, cancellationToken: token);

    /// <summary>保持源Trim及LIKE字面转义，空白不限制筛选。</summary>
    private static string? ToLikePattern(string? value) => string.IsNullOrWhiteSpace(value) ? null : "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    /// <summary>保留SQL Server uniqueidentifier排序的字节优先级，不采用PG原生uuid顺序。</summary>
    private static string SourceUuidOrder(string column) => "ARRAY[" + string.Join(",", new[] { 10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0 }.Select(offset => $"get_byte(uuid_send({column}),{offset})")) + "]";

    /// <summary>本仓储局部显式绑定nullable时间/UUID/布尔/int数组，避免Dapper展开或猜测类型。</summary>
    private sealed class ManagerParameters : SqlMapper.IDynamicParameters
    {
        private readonly List<NpgsqlParameter> values = [];
        /// <summary>固定提供者类型，空值不依赖驱动推断。</summary>
        public void Add(string name, object? value, NpgsqlDbType type) => values.Add(new(name, type) { Value = value ?? DBNull.Value });
        /// <summary>保留本地墙上时间，仅剥离Kind，不进行时区换算。</summary>
        public void Timestamp(string name, DateTime? value) => Add(name, value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : null, NpgsqlDbType.Timestamp);
        /// <summary>去重多选，空集合保留源哨兵与独立开关，不修改请求。</summary>
        public void Statuses(string name, IEnumerable<int> source)
        {
            var distinct = source.Distinct().ToArray(); Add("Limit" + name, distinct.Length > 0, NpgsqlDbType.Boolean);
            Add(name, distinct.Length == 0 ? new[] { -1 } : distinct, NpgsqlDbType.Array | NpgsqlDbType.Integer);
        }
        /// <summary>每个命令持有参数克隆，不接管连接或事务。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity) { foreach (var value in values) command.Parameters.Add(value.Clone()); }
    }

    // 收货先按采购行聚合，不展开需求Link；全部数量、NULL三值和状态优先级由SQL计算。
    private static readonly string SearchSql = $"""
        WITH receipt_facts AS (
            SELECT rl.source_line_id,
                SUM(CASE WHEN rh.posting_status=1 THEN rl.quantity ELSE 0 END) AS received_qty,
                MAX(CASE WHEN rh.posting_status=1 AND (NULLIF(btrim(rl.unit_snapshot,' '),'') IS NULL
                    OR rtrim(rl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci<>rtrim(pl.unit_snapshot,' ') COLLATE public.erp_dictionary_ci)
                    THEN 1 ELSE 0 END) AS has_unit_mismatch
            FROM public.inbound_document_line rl JOIN public.inbound_document rh ON rh.uid=rl.inbound_document_id
            LEFT JOIN public.purchase_order_line pl ON pl.uid=rl.source_line_id
            WHERE rh.document_type=@PurchaseReceipt AND rh.posting_status IN(1,8) AND rl.is_deleted=FALSE AND rh.is_deleted=FALSE
            GROUP BY rl.source_line_id
        ), line_facts AS (
            SELECT l.purchase_order_id,l.uid,l.quantity,l.unit_snapshot,l.tax_amount,l.planned_delivery_date,
                latest.ready_qty,latest.expected_ready_date,
                CASE WHEN latest.purchase_order_line_id IS NULL THEN 0 ELSE 1 END AS has_progress,
                COALESCE(receipt.received_qty,0) AS received_qty,
                CASE WHEN l.quantity<=0 OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL THEN 1 ELSE 0 END AS invalid_base_fact,
                CASE WHEN l.quantity<=0 OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL OR latest.ready_qty<0 OR latest.ready_qty>l.quantity THEN 1 ELSE 0 END AS invalid_ready_fact,
                CASE WHEN l.quantity<=0 OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL OR COALESCE(receipt.has_unit_mismatch,0)=1
                    OR COALESCE(receipt.received_qty,0)<0 OR COALESCE(receipt.received_qty,0)>l.quantity THEN 1 ELSE 0 END AS invalid_receipt_fact,
                CASE WHEN l.quantity>0 AND NULLIF(btrim(l.unit_snapshot,' '),'') IS NOT NULL
                    AND (latest.ready_qty IS NULL OR (latest.ready_qty>=0 AND latest.ready_qty<l.quantity)) AND l.planned_delivery_date IS NOT NULL
                    AND ((@DueView=1 AND l.planned_delivery_date<@BusinessToday)
                        OR (@DueView=2 AND l.planned_delivery_date>=@BusinessToday AND l.planned_delivery_date<@DueSoonExclusive)) THEN 1 ELSE 0 END AS due_hit
            FROM public.purchase_order_line l
            LEFT JOIN LATERAL (SELECT f.purchase_order_line_id,f.ready_qty,f.expected_ready_date FROM public.supplier_progress_report f
                WHERE f.purchase_order_line_id=l.uid AND f.is_deleted=FALSE
                ORDER BY f.sequence DESC NULLS LAST,{SourceUuidOrder("f.uid")} DESC LIMIT 1) latest ON TRUE
            LEFT JOIN receipt_facts receipt ON receipt.source_line_id=l.uid WHERE l.is_deleted=FALSE
        ), head_facts AS (
            SELECT h.uid AS purchase_order_id,h.version,h.lifecycle_status,h.order_no,h.bo_no,h.customer_code,h.supplier_name,h.confirmation_status,
                h.order_date,h.expected_date,h.reply_date,COUNT(f.uid)::integer AS line_count,
                SUM(f.ready_qty) AS raw_ready_qty,SUM(f.quantity) AS raw_total_qty,SUM(f.received_qty) AS raw_received_qty,
                MIN(f.unit_snapshot COLLATE public.erp_dictionary_ci) AS min_unit,MAX(f.unit_snapshot COLLATE public.erp_dictionary_ci) AS max_unit,
                COALESCE(SUM(f.invalid_base_fact),0) AS invalid_base_count,COALESCE(SUM(f.invalid_ready_fact),0) AS invalid_ready_count,
                COALESCE(SUM(f.invalid_receipt_fact),0) AS invalid_receipt_count,
                COALESCE(SUM(CASE WHEN f.uid IS NOT NULL AND f.ready_qty IS NULL THEN 1 ELSE 0 END),0) AS unknown_ready_count,
                COALESCE(SUM(CASE WHEN f.uid IS NOT NULL AND f.ready_qty=0 THEN 1 ELSE 0 END),0) AS zero_ready_count,
                COALESCE(SUM(CASE WHEN f.uid IS NOT NULL AND f.ready_qty=f.quantity THEN 1 ELSE 0 END),0) AS full_ready_count,
                COALESCE(SUM(CASE WHEN f.uid IS NOT NULL AND f.received_qty=0 THEN 1 ELSE 0 END),0) AS zero_receipt_count,
                COALESCE(SUM(CASE WHEN f.uid IS NOT NULL AND f.received_qty=f.quantity THEN 1 ELSE 0 END),0) AS full_receipt_count,
                COALESCE(SUM(f.due_hit),0)::integer AS due_hit_line_count,MIN(CASE WHEN f.due_hit=1 THEN f.planned_delivery_date END) AS earliest_due_hit_date,
                COALESCE(SUM(CASE WHEN f.due_hit=1 AND f.ready_qty IS NULL THEN 1 ELSE 0 END),0) AS unconfirmed_due_count,
                COALESCE(SUM(f.tax_amount),0) AS tax_total_amount,COALESCE(SUM(f.has_progress),0)::integer AS reported_line_count,
                MAX(f.expected_ready_date) AS expected_completion_date
            FROM public.purchase_order_head h LEFT JOIN line_facts f ON f.purchase_order_id=h.uid
            WHERE h.is_deleted=FALSE
                AND (NOT @OnlyWithUnlinkedQuantity OR (h.lifecycle_status=@ActiveLifecycle AND h.confirmation_status IN(@PendingConfirmation,@Confirmed)
                    AND EXISTS(SELECT 1 FROM public.purchase_order_line ul WHERE ul.purchase_order_id=h.uid AND ul.is_deleted=FALSE AND ul.unlinked_qty>0)))
                AND (@Keyword IS NULL OR h.order_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.bo_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.demand_source_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.customer_code COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h.supplier_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR EXISTS(SELECT 1 FROM public.purchase_order_line kl WHERE kl.purchase_order_id=h.uid AND kl.is_deleted=FALSE
                        AND (kl.sku COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR kl.source_no COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~')))
                AND (@CustomerCode IS NULL OR rtrim(h.customer_code,' ') COLLATE public.erp_dictionary_ci=rtrim(@CustomerCode::text,' ') COLLATE public.erp_dictionary_ci)
                AND (@SupplierId IS NULL OR h.supplier_id=@SupplierId)
                AND (@OrderDateFrom IS NULL OR h.order_date>=@OrderDateFrom) AND (@OrderDateToExclusive IS NULL OR h.order_date<@OrderDateToExclusive)
                AND (NOT @LimitConfirmationStatuses OR h.confirmation_status=ANY(@ConfirmationStatuses))
                AND (NOT @LimitLifecycleStatuses OR h.lifecycle_status=ANY(@LifecycleStatuses))
                AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS(SELECT 1 FROM public.purchase_order_line dl
                    WHERE dl.purchase_order_id=h.uid AND dl.is_deleted=FALSE AND (@PlannedDeliveryDateFrom IS NULL OR dl.planned_delivery_date>=@PlannedDeliveryDateFrom)
                    AND (@PlannedDeliveryDateTo IS NULL OR dl.planned_delivery_date<=@PlannedDeliveryDateTo)))
            GROUP BY h.uid,h.version,h.lifecycle_status,h.order_no,h.bo_no,h.customer_code,h.supplier_name,h.confirmation_status,h.order_date,h.expected_date,h.reply_date
        ), projected AS (
            SELECT facts.purchase_order_id,facts.version,facts.lifecycle_status,facts.order_no,facts.bo_no,facts.customer_code,facts.supplier_name,
                facts.confirmation_status,facts.order_date,facts.expected_date,facts.reply_date,facts.line_count,facts.due_hit_line_count,
                facts.earliest_due_hit_date,facts.tax_total_amount,facts.reported_line_count,facts.expected_completion_date,
                CASE WHEN facts.invalid_ready_count>0 OR rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci OR facts.unknown_ready_count>0 OR facts.line_count=0 THEN NULL ELSE facts.raw_ready_qty END AS ready_qty,
                CASE WHEN facts.invalid_base_count>0 OR rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci OR facts.line_count=0 THEN NULL ELSE facts.raw_total_qty END AS total_qty,
                CASE WHEN facts.invalid_receipt_count>0 OR rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci OR facts.line_count=0 THEN NULL
                    ELSE CAST(facts.raw_received_qty/NULLIF(facts.raw_total_qty,0) AS decimal(18,6)) END AS receipt_progress,
                CASE WHEN facts.invalid_base_count>0 OR rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci OR facts.line_count=0 THEN NULL ELSE facts.min_unit END AS unit_snapshot,
                CASE WHEN facts.line_count=0 THEN 0 WHEN facts.invalid_ready_count>0 THEN 9 WHEN facts.reported_line_count=0 THEN 1
                    WHEN facts.unknown_ready_count>0 THEN 2 WHEN facts.zero_ready_count=facts.line_count THEN 3 WHEN facts.full_ready_count=facts.line_count THEN 5 ELSE 4 END AS ready_status,
                CASE WHEN facts.line_count=0 THEN 0 WHEN facts.invalid_receipt_count>0 THEN 9 WHEN facts.zero_receipt_count=facts.line_count THEN 1
                    WHEN facts.full_receipt_count=facts.line_count THEN 3 ELSE 2 END AS receipt_status,
                CASE WHEN facts.unconfirmed_due_count>0 THEN TRUE ELSE FALSE END AS has_unconfirmed_ready_risk,
                CASE WHEN facts.invalid_base_count>0 OR facts.invalid_ready_count>0 OR facts.invalid_receipt_count>0 THEN '采购数量、单位、货好或收货事实不一致，需逐行核实。'
                    WHEN rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci THEN '采购行单位不同，不提供跨行数量合计。'
                    WHEN facts.unknown_ready_count>0 THEN '部分采购行尚未反馈货好数量。' ELSE NULL END AS review_reason,
                CASE WHEN facts.lifecycle_status=@ActiveLifecycle AND facts.confirmation_status=@PendingConfirmation THEN TRUE ELSE FALSE END AS can_confirm,
                CASE WHEN facts.lifecycle_status=@ActiveLifecycle AND facts.confirmation_status=@Confirmed THEN TRUE ELSE FALSE END AS can_cancel_confirmation,
                CASE WHEN facts.lifecycle_status<>@ActiveLifecycle THEN '采购单不是活动状态。' ELSE NULL END AS command_blocked_reason
            FROM head_facts facts
            WHERE (@ExpectedCompletionDateFrom IS NULL OR facts.expected_completion_date>=@ExpectedCompletionDateFrom)
                AND (@ExpectedCompletionDateTo IS NULL OR facts.expected_completion_date<=@ExpectedCompletionDateTo)
                AND (@DueView=0 OR (facts.lifecycle_status=@ActiveLifecycle AND facts.confirmation_status=@Confirmed AND facts.due_hit_line_count>0))
        )
        SELECT p.purchase_order_id AS "PurchaseOrderId",p.version AS "Version",p.lifecycle_status AS "LifecycleStatus",p.order_no AS "OrderNo",p.bo_no AS "BoNo",
            p.customer_code AS "CustomerCode",p.supplier_name AS "SupplierName",p.confirmation_status AS "ConfirmationStatus",p.order_date AS "OrderDate",
            p.expected_date AS "ExpectedDate",p.reply_date AS "ReplyDate",p.line_count AS "LineCount",p.ready_qty AS "ReadyQty",p.total_qty AS "TotalQty",
            p.receipt_progress AS "ReceiptProgress",p.unit_snapshot AS "UnitSnapshot",p.ready_status AS "ReadyStatus",p.receipt_status AS "ReceiptStatus",p.review_reason AS "ReviewReason",
            p.due_hit_line_count AS "DueHitLineCount",p.earliest_due_hit_date AS "EarliestDueHitDate",p.has_unconfirmed_ready_risk AS "HasUnconfirmedReadyRisk",
            p.tax_total_amount AS "TaxTotalAmount",p.reported_line_count AS "ReportedLineCount",p.expected_completion_date AS "ExpectedCompletionDate",
            p.can_confirm AS "CanConfirm",p.can_cancel_confirmation AS "CanCancelConfirmation",p.command_blocked_reason AS "CommandBlockedReason"
        FROM projected p WHERE (NOT @LimitReadyStatuses OR p.ready_status=ANY(@ReadyStatuses)) AND (NOT @LimitReceiptStatuses OR p.receipt_status=ANY(@ReceiptStatuses))
        ORDER BY p.order_date DESC NULLS LAST,p.order_no COLLATE public.erp_dictionary_ci DESC NULLS LAST,{SourceUuidOrder("p.purchase_order_id")} DESC;
        """;

    // 图片取当前产品，送达名仅客供件；从表不隐藏单位不同或超量的真实数量。
    private static readonly string LinesSql = $"""
        WITH receipt_facts AS (
            SELECT rl.source_line_id,SUM(CASE WHEN rh.posting_status=1 THEN rl.quantity ELSE 0 END) AS received_qty
            FROM public.inbound_document_line rl JOIN public.inbound_document rh ON rh.uid=rl.inbound_document_id
            WHERE rh.document_type=@PurchaseReceipt AND rh.posting_status IN(1,8) AND rl.is_deleted=FALSE AND rh.is_deleted=FALSE GROUP BY rl.source_line_id
        )
        SELECT l.uid AS "PurchaseOrderLineId",l.version AS "Version",COALESCE(l.product_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "ProductId",
            l.line_number AS "LineNumber",product.main_image AS "MainImage",l.customer_sku AS "CustomerSku",l.sku AS "Sku",l.link_sku AS "LinkSku",
            l.supplier_sku AS "SupplierSku",l.description_zh AS "DescriptionZh",l.finish_zh AS "FinishZh",l.features_zh AS "FeaturesZh",
            l.material_type AS "MaterialType",l.supply_type AS "SupplyType",l.quantity AS "Quantity",l.unit_snapshot AS "UnitSnapshot",
            COALESCE(receipt.received_qty,0) AS "ReceivedQty",CASE WHEN l.quantity>COALESCE(receipt.received_qty,0) THEN l.quantity-COALESCE(receipt.received_qty,0) ELSE 0 END AS "UnreceivedQty",
            latest.ready_qty AS "ReadyQty",l.planned_delivery_date AS "PlannedDeliveryDate",latest.expected_ready_date AS "ExpectedCompletionDate",
            latest.supplier_reported_date AS "LatestFollowUpDate",latest.created_by AS "FollowUpUserName",l.unit_price AS "UnitPrice",l.tax_price AS "TaxPrice",
            l.unit_amount AS "UnitAmount",l.tax_amount AS "TaxAmount",
            CASE WHEN rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=@CustomerSuppliedMaterialType THEN l.delivery_supplier_name_snapshot ELSE NULL END AS "DeliverySupplierName",
            l.source_type AS "SourceType",l.source_no AS "SourceNo",l.source_line_number AS "SourceLineNumber",l.sub_note AS "SubNote"
        FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
        LEFT JOIN public.product product ON product.uid=l.product_id AND product.is_deleted=FALSE
        LEFT JOIN LATERAL (SELECT f.ready_qty,f.expected_ready_date,f.supplier_reported_date,f.created_by FROM public.supplier_progress_report f
            WHERE f.purchase_order_line_id=l.uid AND f.is_deleted=FALSE ORDER BY f.sequence DESC NULLS LAST,{SourceUuidOrder("f.uid")} DESC LIMIT 1) latest ON TRUE
        LEFT JOIN receipt_facts receipt ON receipt.source_line_id=l.uid
        WHERE h.uid=@PurchaseOrderId AND h.is_deleted=FALSE AND l.is_deleted=FALSE ORDER BY l.line_number,{SourceUuidOrder("l.uid")};
        """;
}

