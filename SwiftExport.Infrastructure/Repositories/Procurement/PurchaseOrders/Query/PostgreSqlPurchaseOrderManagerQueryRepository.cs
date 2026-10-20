using System.Data;
using System.Text.Json;
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

/// <summary>采购管理只读投影：数据库完成筛选、计数和分页，仅返回订单事实与命令资格，不接管事务。</summary>
public sealed class PostgreSqlPurchaseOrderManagerQueryRepository(RepositoryBaseContext context) : IPurchaseOrderManagerQueryRepository
{
    /// <summary>SQL完成商业计算、筛选、准确计数和稳定分页；只传输当前页，空页仍保留总量。</summary>
    public async Task<PagedResponseBase<PurchaseOrderManagerListItemDto>> SearchAsync(PurchaseOrderManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);cancellationToken.ThrowIfCancellationRequested();
        var p = new ManagerParameters();
        p.Add("Keyword", ToLikePattern(request.Keyword), NpgsqlDbType.Text);
        p.Add("CustomerCode", string.IsNullOrWhiteSpace(request.CustomerCode) ? null : request.CustomerCode.Trim(), NpgsqlDbType.Text);
        p.Add("SupplierId", request.SupplierId, NpgsqlDbType.Uuid);
        p.Add("CustomerCodes",request.CustomerCodes.Distinct().Select(value=>value.Trim()).ToArray(),NpgsqlDbType.Array|NpgsqlDbType.Text);
        p.Add("SupplierIds",request.SupplierIds.Distinct().ToArray(),NpgsqlDbType.Array|NpgsqlDbType.Uuid);
        p.Add("OnlyWithUnlinkedQuantity", request.OnlyWithUnlinkedQuantity, NpgsqlDbType.Boolean);
        p.Timestamp("OrderDateFrom", request.OrderDateFrom?.Date);
        p.Timestamp("OrderDateToExclusive", request.OrderDateTo.HasValue && request.OrderDateTo.Value.Date < DateTime.MaxValue.Date ? request.OrderDateTo.Value.Date.AddDays(1) : null);
        p.Statuses("ConfirmationStatuses", request.ConfirmationStatuses.Select(x => (int)x));
        p.Statuses("LifecycleStatuses", request.LifecycleStatuses.Select(x => (int)x));
        p.Timestamp("PlannedDeliveryDateFrom", request.PlannedDeliveryDateFrom); p.Timestamp("PlannedDeliveryDateTo", request.PlannedDeliveryDateTo);
        p.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt, NpgsqlDbType.Integer);
        p.Add("ActiveLifecycle", (int)PurchaseOrderLifecycleStatusEnum.Active, NpgsqlDbType.Integer);
        p.Add("PendingConfirmation", (int)ConfirmationStatusEnum.待确认, NpgsqlDbType.Integer);
        p.Add("Confirmed", (int)ConfirmationStatusEnum.已确认, NpgsqlDbType.Integer);
        p.Add("PageSize",request.PageSize,NpgsqlDbType.Integer);p.Add("Offset",(long)(request.PageIndex-1)*request.PageSize,NpgsqlDbType.Bigint);
        var page=await context.Session.Connection.QuerySingleAsync<ManagerPageRow>(Command(SearchSql,p,cancellationToken));
        return new() { TotalCount=page.TotalCount,Items=JsonSerializer.Deserialize<List<PurchaseOrderManagerListItemDto>>(page.ItemsJson)??[],PageIndex=request.PageIndex,PageSize=request.PageSize };
    }

    /// <summary>从表保留采购行商业快照、来源关联和当前图片，不采用头的跨单位遮罩。</summary>
    public async Task<IReadOnlyList<PurchaseOrderManagerLineDto>> GetLinesAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var p = new ManagerParameters(); p.Add("PurchaseOrderId", purchaseOrderId, NpgsqlDbType.Uuid);
        p.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt, NpgsqlDbType.Integer);
        p.Add("CustomerSuppliedMaterialType", MaterialTypesEnum.客供件, NpgsqlDbType.Text);
        return (await context.Session.Connection.QueryAsync<PurchaseOrderManagerLineDto>(Command(LinesSql, p, cancellationToken))).ToList();
    }

    /// <summary>单SQL返回总量及当前页JSON，避免逐头取明细或在应用内全量分页。</summary>
    private sealed class ManagerPageRow
    {
        public int TotalCount { get; set; }
        public string ItemsJson { get; set; } = "[]";
    }

    private const string ManagerColumns="""
        "PurchaseOrderId","Version","LifecycleStatus","OrderNo","BoNo","CustomerCode","SupplierName","ConfirmationStatus",
        "OrderDate","ExpectedDate","ReplyDate","LineCount","TotalQty","UnitSnapshot","TaxTotalAmount",
        "CanConfirm","CanCancelConfirmation","CommandBlockedReason","CanDeleteDraft","DeleteDraftBlockReason"
        """;

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

    // 事实合计不读取供应商反馈或正式收货；删除资格仍保留既有下游历史阻断。
    private static readonly string SearchSql = $"""
        WITH {PurchaseOrderManagerAssociationSql.OrphanHeadsCte}, line_facts AS (
            SELECT l.purchase_order_id,l.uid,l.quantity,l.unit_snapshot,l.tax_amount,
                CASE WHEN l.quantity<=0 OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL THEN 1 ELSE 0 END AS invalid_base_fact
            FROM public.purchase_order_line l WHERE l.is_deleted=FALSE
        ), head_facts AS (
            SELECT h.uid AS purchase_order_id,h.version,h.lifecycle_status,h.order_no,h.bo_no,h.customer_code,h.supplier_name,h.confirmation_status,
                h.order_date,h.expected_date,h.reply_date,COUNT(f.uid)::integer AS line_count,
                SUM(f.quantity) AS raw_total_qty,
                MIN(f.unit_snapshot COLLATE public.erp_dictionary_ci) AS min_unit,MAX(f.unit_snapshot COLLATE public.erp_dictionary_ci) AS max_unit,
                COALESCE(SUM(f.invalid_base_fact),0) AS invalid_base_count,COALESCE(SUM(f.tax_amount),0) AS tax_total_amount,
                MAX(CASE WHEN orphan.head_id IS NOT NULL THEN 1 ELSE 0 END) AS orphan_association,
                {PurchaseDraftDeletionSql.BlockReason("h")} AS deletion_block_reason
            FROM public.purchase_order_head h LEFT JOIN line_facts f ON f.purchase_order_id=h.uid
            LEFT JOIN manager_orphan_association_heads orphan ON orphan.head_id=h.uid
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
                AND (cardinality(@CustomerCodes)=0 OR rtrim(h.customer_code,' ') COLLATE public.erp_dictionary_ci=ANY(@CustomerCodes))
                AND (cardinality(@SupplierIds)=0 OR h.supplier_id=ANY(@SupplierIds))
                AND (@OrderDateFrom IS NULL OR h.order_date>=@OrderDateFrom) AND (@OrderDateToExclusive IS NULL OR h.order_date<@OrderDateToExclusive)
                AND (NOT @LimitConfirmationStatuses OR h.confirmation_status=ANY(@ConfirmationStatuses))
                AND (NOT @LimitLifecycleStatuses OR h.lifecycle_status=ANY(@LifecycleStatuses))
                AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS(SELECT 1 FROM public.purchase_order_line dl
                    WHERE dl.purchase_order_id=h.uid AND dl.is_deleted=FALSE AND (@PlannedDeliveryDateFrom IS NULL OR dl.planned_delivery_date>=@PlannedDeliveryDateFrom)
                    AND (@PlannedDeliveryDateTo IS NULL OR dl.planned_delivery_date<=@PlannedDeliveryDateTo)))
            GROUP BY h.uid,h.version,h.lifecycle_status,h.order_no,h.bo_no,h.customer_code,h.supplier_name,h.confirmation_status,h.order_date,h.expected_date,h.reply_date,h.is_deleted,h.active_confirmation_id,h.confirmed_date
        ), projected AS (
            SELECT facts.*,
                CASE WHEN facts.invalid_base_count>0 OR rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci OR facts.line_count=0 THEN NULL ELSE facts.raw_total_qty END AS total_qty,
                CASE WHEN facts.invalid_base_count>0 OR rtrim(facts.min_unit,' ') COLLATE public.erp_dictionary_ci<>rtrim(facts.max_unit,' ') COLLATE public.erp_dictionary_ci OR facts.line_count=0 THEN NULL ELSE facts.min_unit END AS unit_snapshot,
                CASE WHEN facts.lifecycle_status=@ActiveLifecycle AND facts.confirmation_status=@PendingConfirmation AND facts.orphan_association=0 THEN TRUE ELSE FALSE END AS can_confirm,
                CASE WHEN facts.lifecycle_status=@ActiveLifecycle AND facts.confirmation_status=@Confirmed THEN TRUE ELSE FALSE END AS can_cancel_confirmation,
                CASE WHEN facts.lifecycle_status<>@ActiveLifecycle THEN '采购单不是活动状态。' WHEN facts.orphan_association>0 THEN '需求引用目标需核实。' ELSE NULL END AS command_blocked_reason
            FROM head_facts facts
        ), filtered AS (
        SELECT p.purchase_order_id AS "PurchaseOrderId",p.version AS "Version",p.lifecycle_status AS "LifecycleStatus",p.order_no AS "OrderNo",p.bo_no AS "BoNo",
            p.customer_code AS "CustomerCode",p.supplier_name AS "SupplierName",p.confirmation_status AS "ConfirmationStatus",p.order_date AS "OrderDate",
            p.expected_date AS "ExpectedDate",p.reply_date AS "ReplyDate",p.line_count AS "LineCount",p.total_qty AS "TotalQty",
            p.unit_snapshot AS "UnitSnapshot",p.tax_total_amount AS "TaxTotalAmount",
            p.can_confirm AS "CanConfirm",p.can_cancel_confirmation AS "CanCancelConfirmation",p.command_blocked_reason AS "CommandBlockedReason",
            (p.deletion_block_reason='' AND p.line_count>0 AND p.orphan_association=0) AS "CanDeleteDraft",
            CASE WHEN p.deletion_block_reason<>'' THEN p.deletion_block_reason WHEN p.line_count=0 THEN '采购单缺少有效明细'
                WHEN p.orphan_association>0 THEN '需求引用目标需核实' ELSE '' END AS "DeleteDraftBlockReason"
        FROM projected p
        ), page AS (
            SELECT {ManagerColumns} FROM filtered
            ORDER BY "OrderDate" DESC NULLS LAST,"OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,{SourceUuidOrder("\"PurchaseOrderId\"")} DESC
            LIMIT @PageSize OFFSET @Offset
        )
        SELECT (SELECT COUNT(*)::integer FROM filtered) AS "TotalCount",
            COALESCE((SELECT jsonb_agg(to_jsonb(page) ORDER BY "OrderDate" DESC NULLS LAST,
                "OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,{SourceUuidOrder("\"PurchaseOrderId\"")} DESC) FROM page),'[]'::jsonb)::text AS "ItemsJson";
        """;

    // 图片取当前产品，送达名仅客供件；从表不隐藏单位不同或超量的真实数量。
    private static readonly string LinesSql = $"""
        WITH {PurchaseOrderManagerAssociationSql.Ctes}
        SELECT l.uid AS "PurchaseOrderLineId",l.version AS "Version",COALESCE(l.product_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "ProductId",
            l.line_number AS "LineNumber",product.main_image AS "MainImage",l.customer_sku AS "CustomerSku",l.sku AS "Sku",l.link_sku AS "LinkSku",
            l.supplier_sku AS "SupplierSku",l.description_zh AS "DescriptionZh",l.finish_zh AS "FinishZh",l.features_zh AS "FeaturesZh",
            l.material_type AS "MaterialType",l.supply_type AS "SupplyType",l.quantity AS "Quantity",l.unit_snapshot AS "UnitSnapshot",
            CASE WHEN association.historical_only OR association.review_reason IS NOT NULL THEN NULL ELSE COALESCE(association.linked_qty,0) END AS "LinkedDemandQty",
            CASE WHEN association.historical_only OR association.review_reason IS NOT NULL THEN NULL ELSE l.unlinked_qty END AS "UnlinkedQty",
            association.linked_count AS "LinkedDemandCount",
            CASE WHEN association.historical_only THEN 8 WHEN association.review_reason IS NOT NULL THEN 9 WHEN COALESCE(association.linked_qty,0)=0 THEN 0
                WHEN association.linked_qty<l.quantity THEN 1 ELSE 2 END AS "AssociationStatus",
            association.review_reason AS "AssociationReviewReason",
            COALESCE(association.source_summary,'未关联物料需求') AS "SourceSummary",
            l.planned_delivery_date AS "PlannedDeliveryDate",l.unit_price AS "UnitPrice",l.tax_price AS "TaxPrice",
            l.unit_amount AS "UnitAmount",l.tax_amount AS "TaxAmount",
            CASE WHEN rtrim(l.material_type,' ') COLLATE public.erp_dictionary_ci=@CustomerSuppliedMaterialType THEN l.delivery_supplier_name_snapshot ELSE NULL END AS "DeliverySupplierName",
            CASE WHEN association.historical_count>0 THEN association.source_type ELSE l.source_type END AS "SourceType",
            CASE WHEN association.historical_count>0 THEN association.source_no ELSE l.source_no END AS "SourceNo",
            CASE WHEN association.historical_count>0 THEN association.source_line_number ELSE l.source_line_number END AS "SourceLineNumber",l.sub_note AS "SubNote"
        FROM public.purchase_order_line l JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
        LEFT JOIN public.product product ON product.uid=l.product_id AND product.is_deleted=FALSE
        LEFT JOIN manager_association_checked association ON association.line_id=l.uid
        WHERE h.uid=@PurchaseOrderId AND h.is_deleted=FALSE AND l.is_deleted=FALSE ORDER BY l.line_number,{SourceUuidOrder("l.uid")};
        """;
}

