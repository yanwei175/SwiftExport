using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;

/// <summary>供应商反馈录入选单投影；只读取采购和反馈，数据库完成筛选、计数和稳定分页。</summary>
public sealed partial class PostgreSqlPurchaseOrderProgressQueryRepository(RepositoryBaseContext context)
{
    private static readonly EntityMetadata ReportMap = EntityMetadataCache.GetMetadata(typeof(SupplierProgressReport));

    /// <summary>借用当前连接及事务；同一SQL快照计算总数和当前页，不读取入库或库存历史。</summary>
    public async Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(
        PurchaseOrderProgressSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        foreach (var pair in new[] { ("Keyword", request.Keyword), ("OrderNo", request.OrderNo), ("SupplierKeyword", request.SupplierKeyword), ("Sku", request.Sku) })
            parameters.Add(pair.Item1, ToLikePattern(pair.Item2));
        AddStatuses(parameters, "Confirmation", request.ConfirmationStatuses.Select(x => (int)x).ToArray());
        AddStatuses(parameters, "Lifecycle", request.LifecycleStatuses.Select(x => (int)x).ToArray());
        AddStatuses(parameters, "Arrangement", request.ArrangementStatuses.Select(x => (int)x).ToArray());
        parameters.AddTimestamp("PlannedDeliveryDateFrom", request.PlannedDeliveryDateFrom);
        parameters.AddTimestamp("PlannedDeliveryDateTo", request.PlannedDeliveryDateTo);
        parameters.AddTimestamp("ExpectedReadyDateFrom", request.ExpectedReadyDateFrom);
        parameters.AddTimestamp("ExpectedReadyDateTo", request.ExpectedReadyDateTo);
        parameters.Add("Active", (int)PurchaseOrderLifecycleStatusEnum.Active);
        parameters.Add("Confirmed", (int)ConfirmationStatusEnum.已确认);
        parameters.Add("PageSize", request.PageSize);
        parameters.Add("Offset", (long)(request.PageIndex - 1) * request.PageSize);
        var page = await context.Session.Connection.QuerySingleAsync<ProgressPageRow>(new CommandDefinition(
            SearchSql(), new SearchParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return new() { TotalCount = page.TotalCount, Items = JsonSerializer.Deserialize<List<PurchaseOrderProgressListItemDto>>(page.ItemsJson) ?? [], PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>空状态仍传源哨兵-1但关闭限制；枚举去重不增加新状态资格。</summary>
    private static void AddStatuses(PostgreSqlParameters parameters, string dimension, int[] values)
    {
        parameters.Add(dimension + "Statuses", values.Length == 0 ? new[] { -1 } : values.Distinct().ToArray());
        parameters.Add("Limit" + dimension + "Statuses", values.Length > 0);
    }

    /// <summary>仅清理外部空白并转义限定LIKE字符，问号和内部空格保留字面。</summary>
    private static string? ToLikePattern(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";

    /// <summary>全部商业筛选、整行最新反馈及汇总在一个SQL完成；输出已按源排序，不重查商业事实。</summary>
    private static string SearchSql() => $"""
        WITH LineFacts AS MATERIALIZED
        (
            SELECT l."purchase_order_id" AS purchase_order_id,l."uid" AS uid,
                l."quantity" AS quantity,l."planned_delivery_date" AS planned_delivery_date,
                latest.arrangement_status,latest.ready_qty,latest.expected_ready_date,
                CASE WHEN latest.sequence IS NULL THEN 0 ELSE 1 END AS has_progress
            FROM "public"."purchase_order_line" l
            LEFT JOIN LATERAL
            (
                SELECT {R(nameof(SupplierProgressReport.Sequence))} AS sequence,{R(nameof(SupplierProgressReport.ArrangementStatus))} AS arrangement_status,
                    {R(nameof(SupplierProgressReport.ReadyQty))} AS ready_qty,{R(nameof(SupplierProgressReport.ExpectedReadyDate))} AS expected_ready_date
                FROM "public"."supplier_progress_report" r
                WHERE {R(nameof(SupplierProgressReport.PurchaseOrderLineId))}=l."uid"
                    AND COALESCE({R(nameof(SupplierProgressReport.IsDeleted))},FALSE)=FALSE
                ORDER BY {R(nameof(SupplierProgressReport.Sequence))} DESC NULLS LAST,{UuidOrder(R(nameof(SupplierProgressReport.Uid)))} DESC LIMIT 1
            ) latest ON TRUE
            WHERE COALESCE(l."is_deleted",FALSE)=FALSE
        ), HeadFacts AS
        (
            SELECT h."uid" AS "PurchaseOrderId",h."version" AS "Version",
                h."confirmation_status" AS "ConfirmationStatus",h."lifecycle_status" AS "LifecycleStatus",
                h."order_no" AS "OrderNo",h."order_date" AS "OrderDate",
                h."bo_no" AS "BoNo",h."supplier_name" AS "SupplierName",
                h."demand_source_no" AS "DemandSourceNo",
                COUNT(f.uid)::integer AS "LineCount",COALESCE(SUM(f.has_progress),0)::integer AS "ReportedLineCount",
                SUM(CASE WHEN f.uid IS NOT NULL AND f.ready_qty IS NULL THEN 1 ELSE 0 END)::integer AS "UnknownReadyQtyLineCount",
                COALESCE(SUM(f.quantity),0) AS "TotalQty",COALESCE(SUM(f.ready_qty),0) AS "LatestReadyQty",
                MAX(f.expected_ready_date) AS "LatestExpectedReadyDate"
            FROM "public"."purchase_order_head" h LEFT JOIN LineFacts f ON f.purchase_order_id=h."uid"
            WHERE COALESCE(h."is_deleted",FALSE)=FALSE
                AND (@Keyword IS NULL OR h."order_no" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR h."bo_no" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'
                    OR h."supplier_name" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~' OR EXISTS
                    (SELECT 1 FROM "public"."purchase_order_line" keywordLine WHERE keywordLine."purchase_order_id"=h."uid"
                        AND COALESCE(keywordLine."is_deleted",FALSE)=FALSE AND keywordLine."sku" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'))
                AND (@OrderNo IS NULL OR h."order_no" COLLATE public.erp_dictionary_ci LIKE @OrderNo ESCAPE '~')
                AND (@SupplierKeyword IS NULL OR h."supplier_code" COLLATE public.erp_dictionary_ci LIKE @SupplierKeyword ESCAPE '~' OR h."supplier_name" COLLATE public.erp_dictionary_ci LIKE @SupplierKeyword ESCAPE '~')
                AND (@Sku IS NULL OR EXISTS(SELECT 1 FROM "public"."purchase_order_line" skuLine
                    WHERE skuLine."purchase_order_id"=h."uid"
                    AND COALESCE(skuLine."is_deleted",FALSE)=FALSE AND skuLine."sku" COLLATE public.erp_dictionary_ci LIKE @Sku ESCAPE '~'))
                AND (NOT @LimitConfirmationStatuses OR h."confirmation_status"=ANY(@ConfirmationStatuses))
                AND (NOT @LimitLifecycleStatuses OR h."lifecycle_status"=ANY(@LifecycleStatuses))
                AND (NOT @LimitArrangementStatuses OR EXISTS(SELECT 1 FROM LineFacts arrangementLine
                    WHERE arrangementLine.purchase_order_id=h."uid"
                    AND COALESCE(arrangementLine.arrangement_status,0)=ANY(@ArrangementStatuses)))
                AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS
                    (SELECT 1 FROM "public"."purchase_order_line" plannedLine WHERE plannedLine."purchase_order_id"=h."uid"
                        AND COALESCE(plannedLine."is_deleted",FALSE)=FALSE
                        AND (@PlannedDeliveryDateFrom IS NULL OR plannedLine."planned_delivery_date">=@PlannedDeliveryDateFrom)
                        AND (@PlannedDeliveryDateTo IS NULL OR plannedLine."planned_delivery_date"<@PlannedDeliveryDateTo+INTERVAL '1 day')))
            GROUP BY h."uid",h."version",h."confirmation_status",h."lifecycle_status",
                h."order_no",h."order_date",h."bo_no",h."supplier_name",h."demand_source_no"
        ), filtered AS (
        SELECT facts."PurchaseOrderId" AS "PurchaseOrderId",facts."Version" AS "Version",facts."ConfirmationStatus" AS "ConfirmationStatus",
            facts."LifecycleStatus" AS "LifecycleStatus",facts."OrderNo" AS "OrderNo",facts."OrderDate" AS "OrderDate",facts."BoNo" AS "BoNo",
            facts."SupplierName" AS "SupplierName",facts."DemandSourceNo" AS "DemandSourceNo",facts."LineCount" AS "LineCount",
            facts."ReportedLineCount" AS "ReportedLineCount",facts."UnknownReadyQtyLineCount" AS "UnknownReadyQtyLineCount",
            facts."TotalQty" AS "TotalQty",facts."LatestReadyQty" AS "LatestReadyQty",facts."LatestExpectedReadyDate" AS "LatestExpectedReadyDate",
            CASE WHEN facts."LifecycleStatus"=@Active AND facts."ConfirmationStatus"=@Confirmed THEN TRUE ELSE FALSE END AS "CanReportProgress",
            CASE WHEN facts."LifecycleStatus"<>@Active THEN '仅活动采购单允许登记供应商反馈。'
                WHEN facts."ConfirmationStatus"<>@Confirmed THEN '仅已确认采购单允许登记供应商反馈。' ELSE '' END AS "ReadOnlyReason"
        FROM HeadFacts facts
        WHERE (@ExpectedReadyDateFrom IS NULL OR facts."LatestExpectedReadyDate">=@ExpectedReadyDateFrom)
            AND (@ExpectedReadyDateTo IS NULL OR facts."LatestExpectedReadyDate"<@ExpectedReadyDateTo+INTERVAL '1 day')
        )
        SELECT (SELECT count(*)::int FROM filtered) AS "TotalCount",
            COALESCE((SELECT jsonb_agg(page ORDER BY page."OrderDate" DESC NULLS LAST,
                page."OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,{UuidOrder("page.\"PurchaseOrderId\"")} DESC) FROM (
                SELECT "PurchaseOrderId","Version","ConfirmationStatus","LifecycleStatus","OrderNo","OrderDate","BoNo","SupplierName","DemandSourceNo","LineCount","ReportedLineCount","UnknownReadyQtyLineCount","TotalQty","LatestReadyQty","LatestExpectedReadyDate","CanReportProgress","ReadOnlyReason" FROM filtered facts
                ORDER BY facts."OrderDate" DESC NULLS LAST,facts."OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,
                    {UuidOrder("facts.\"PurchaseOrderId\"")} DESC LIMIT @PageSize OFFSET @Offset
            ) page),'[]'::jsonb)::text AS "ItemsJson"
        """;

    /// <summary>接纳数据库分页回执，超末页仍返回同一筛选范围的总数。</summary>
    private sealed class ProgressPageRow
    {
        public int TotalCount { get; set; }
        public string ItemsJson { get; set; } = "[]";
    }

    /// <summary>以CLR属性定位PG列，包括旧QTY对应quantity，不接受外部标识符。</summary>
    private static string C(EntityMetadata map, string alias, string property) => alias + "." + PostgreSqlNames.Quote(map.Columns.Single(x => x.PropertyName == property).ColumnName);
    private static string R(string property) => C(ReportMap, "r", property);
    /// <summary>保留SQL Server uniqueidentifier尾键顺序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "]";

    /// <summary>仅给本查询明确nullable标量和int数组类型，不修改共享参数类或连接识别。</summary>
    private sealed class SearchParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>克隆后的参数归命令所有；数组不由Dapper展开，时间按墙上时间绑定。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                parameter.NpgsqlDbType = parameter.ParameterName switch
                {
                    "ConfirmationStatuses" or "LifecycleStatuses" or "ArrangementStatuses" => NpgsqlDbType.Array | NpgsqlDbType.Integer,
                    "PlannedDeliveryDateFrom" or "PlannedDeliveryDateTo" or "ExpectedReadyDateFrom" or "ExpectedReadyDateTo" => NpgsqlDbType.Timestamp,
                    "Keyword" or "OrderNo" or "SupplierKeyword" or "Sku" => NpgsqlDbType.Text,
                    "LimitConfirmationStatuses" or "LimitLifecycleStatuses" or "LimitArrangementStatuses" => NpgsqlDbType.Boolean,
                    "Offset" => NpgsqlDbType.Bigint,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
