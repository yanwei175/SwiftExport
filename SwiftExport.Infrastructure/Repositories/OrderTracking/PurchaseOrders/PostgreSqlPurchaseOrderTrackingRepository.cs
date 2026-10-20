using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Core.Interfaces.OrderTracking.PurchaseOrders;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking.PurchaseOrders;

/// <summary>采购单跟踪分页与全量子行，按权限限定实际读取维度，不依赖成品需求关联。</summary>
public sealed class PostgreSqlPurchaseOrderTrackingRepository(RepositoryBaseContext context) : IPurchaseOrderTrackingRepository
{
    /// <summary>SQL完成商业计算、筛选、准确计数和稳定分页；只传输当前页，空页仍保留总量。</summary>
    public async Task<PagedResponseBase<PurchaseOrderTrackingItemDto>> SearchAsync(PurchaseOrderTrackingSearchRequest request, bool canViewProgress, bool canViewReceipts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);cancellationToken.ThrowIfCancellationRequested();
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "China Standard Time").Date;
        var p = new TrackingParameters();
        p.Add("CanViewProgress",canViewProgress,NpgsqlDbType.Boolean);p.Add("CanViewReceipts",canViewReceipts,NpgsqlDbType.Boolean);
        p.Add("Keyword", ToLikePattern(request.Keyword), NpgsqlDbType.Text);
        p.Add("CustomerCodes",request.CustomerCodes.Distinct().Select(value=>value.Trim()).ToArray(),NpgsqlDbType.Array|NpgsqlDbType.Text);
        p.Add("SupplierIds",request.SupplierIds.Distinct().ToArray(),NpgsqlDbType.Array|NpgsqlDbType.Uuid);
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
        p.Add("PageSize",request.PageSize,NpgsqlDbType.Integer);p.Add("Offset",(long)(request.PageIndex-1)*request.PageSize,NpgsqlDbType.Bigint);
        var page=await context.Session.Connection.QuerySingleAsync<TrackingPageRow>(Command(PurchaseOrderTrackingSql.SearchSql,p,cancellationToken));
        return new() { TotalCount=page.TotalCount,Items=JsonSerializer.Deserialize<List<PurchaseOrderTrackingItemDto>>(page.ItemsJson)??[],PageIndex=request.PageIndex,PageSize=request.PageSize };
    }

    /// <summary>从表直接保留采购行数量、未知反馈和当前图片，不采用头的跨单位遮罩。</summary>
    public async Task<IReadOnlyList<PurchaseOrderTrackingLineDto>> GetLinesAsync(Guid purchaseOrderId, bool canViewProgress, bool canViewReceipts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var p = new TrackingParameters();
        p.Add("CanViewProgress",canViewProgress,NpgsqlDbType.Boolean);p.Add("CanViewReceipts",canViewReceipts,NpgsqlDbType.Boolean); p.Add("PurchaseOrderId", purchaseOrderId, NpgsqlDbType.Uuid);
        p.Add("PurchaseReceipt", (int)InboundDocumentTypeEnum.PurchaseReceipt, NpgsqlDbType.Integer);
        p.Add("CustomerSuppliedMaterialType", MaterialTypesEnum.客供件, NpgsqlDbType.Text);
        return (await context.Session.Connection.QueryAsync<PurchaseOrderTrackingLineDto>(Command(PurchaseOrderTrackingSql.LinesSql, p, cancellationToken))).ToList();
    }

    /// <summary>单SQL返回总量及当前页JSON，避免逐头取明细或在应用内全量分页。</summary>
    private sealed class TrackingPageRow
    {
        public int TotalCount { get; set; }
        public string ItemsJson { get; set; } = "[]";
    }

    /// <summary>只借用调用方事务及令牌，不新增事务、连接或锁。</summary>
    private CommandDefinition Command(string sql, TrackingParameters p, CancellationToken token) => new(sql, p, context.Session.Transaction, cancellationToken: token);

    /// <summary>保持源Trim及LIKE字面转义，空白不限制筛选。</summary>
    private static string? ToLikePattern(string? value) => string.IsNullOrWhiteSpace(value) ? null : "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    /// <summary>本仓储局部显式绑定nullable时间/UUID/布尔/int数组，避免Dapper展开或猜测类型。</summary>
    private sealed class TrackingParameters : SqlMapper.IDynamicParameters
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


    /// <inheritdoc/>
    public Task<bool> ExistsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
        =>context.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM public.purchase_order_head WHERE uid=@PurchaseOrderId AND NOT is_deleted)",
            new{PurchaseOrderId=purchaseOrderId},context.Session.Transaction,cancellationToken:cancellationToken));

    /// <inheritdoc/>
    public async Task<PurchaseOrderTrackingReceiptsDto> GetReceiptsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
    {
        var lines=(await context.Session.Connection.QueryAsync<PurchaseLineReceiptProgressDto>(new CommandDefinition($"""
            SELECT h.uid AS "PurchaseOrderId",l.uid AS "PurchaseLineId",h.order_no AS "PurchaseOrderNo",
                l.line_number AS "PurchaseLineNumber",l.sku AS "Sku",l.unit_snapshot AS "Unit",
                CASE WHEN NOT h.is_deleted AND NOT l.is_deleted AND h.confirmation_status=1 AND l.quantity>0
                    AND NULLIF(btrim(l.unit_snapshot,' '),'') IS NOT NULL THEN l.quantity END AS "PurchaseLineQty",
                (SELECT count(DISTINCT link.net_demand_id)>1 FROM public.demand_execution_link link
                    WHERE link.target_line_id=l.uid AND rtrim(link.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                    AND NOT link.is_deleted AND link.link_status=0 AND link.allocated_qty>link.released_qty) AS "IsSharedPurchaseLine",
                CASE WHEN h.is_deleted OR l.is_deleted THEN '采购目标已删除，需核实'
                    WHEN h.confirmation_status<>1 THEN '未确认采购已有入库证据，需核实'
                    WHEN l.quantity<=0 OR NULLIF(btrim(l.unit_snapshot,' '),'') IS NULL THEN '采购数量或单位需核实'
                    ELSE '' END AS "ReviewReason"
            FROM public.purchase_order_head h JOIN public.purchase_order_line l ON l.purchase_order_id=h.uid
            WHERE h.uid=@PurchaseOrderId AND {OrderTrackingReceiptScopeSql.Include("h","l.uid")}
            ORDER BY l.line_number,l.uid
            """,new{PurchaseOrderId=purchaseOrderId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
        var evidence=await PurchaseLineReceiptEvidenceReader.ReadAsync(context,lines,cancellationToken);
        return new(){PurchaseOrderId=purchaseOrderId,Items=evidence.Items,PurchaseLines=lines,ReviewReason=evidence.ReviewReason};
    }

    /// <inheritdoc/>
    public async Task<List<SupplierProgressReportDto>> GetReportsAsync(Guid purchaseOrderId,CancellationToken cancellationToken=default)
        =>(await context.Session.Connection.QueryAsync<SupplierProgressReportDto>(new CommandDefinition("""
            SELECT r.uid AS "Uid",r.purchase_order_line_id AS "PurchaseOrderLineId",r.sequence AS "Sequence",
                r.arrangement_status AS "ArrangementStatus",r.ready_qty AS "ReadyQty",r.expected_ready_date AS "ExpectedReadyDate",
                r.supplier_reported_date AS "SupplierReportedDate",r.note AS "Note",r.is_correction AS "IsCorrection",
                r.correction_reason AS "CorrectionReason",r.command_id AS "CommandId",r.created_date AS "CreatedDate",r.created_by AS "CreatedBy"
            FROM public.supplier_progress_report r JOIN public.purchase_order_line l ON l.uid=r.purchase_order_line_id
            JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            WHERE h.uid=@PurchaseOrderId AND NOT h.is_deleted AND NOT r.is_deleted
            ORDER BY l.line_number,r.sequence DESC NULLS LAST,r.uid DESC
            """,new{PurchaseOrderId=purchaseOrderId},context.Session.Transaction,cancellationToken:cancellationToken))).AsList();
}
