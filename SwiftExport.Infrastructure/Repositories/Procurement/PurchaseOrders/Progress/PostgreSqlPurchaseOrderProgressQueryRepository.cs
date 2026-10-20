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

/// <summary>供应商反馈录入选单投影；只读取订单事实，数据库完成筛选、计数和稳定分页。</summary>
public sealed partial class PostgreSqlPurchaseOrderProgressQueryRepository(RepositoryBaseContext context)
{
    private static readonly EntityMetadata ReportMap = EntityMetadataCache.GetMetadata(typeof(SupplierProgressReport));

    /// <summary>借用当前连接及事务；同一SQL快照计算总数和当前页，不读取供应商报告、入库或库存历史。</summary>
    public async Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(
        PurchaseOrderProgressSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = new PostgreSqlParameters();
        foreach (var pair in new[] { ("Keyword", request.Keyword), ("OrderNo", request.OrderNo), ("SupplierKeyword", request.SupplierKeyword), ("Sku", request.Sku) })
            parameters.Add(pair.Item1, ToLikePattern(pair.Item2));
        AddStatuses(parameters, "Confirmation", request.ConfirmationStatuses.Select(x => (int)x).ToArray());
        AddStatuses(parameters, "Lifecycle", request.LifecycleStatuses.Select(x => (int)x).ToArray());
        parameters.AddTimestamp("PlannedDeliveryDateFrom", request.PlannedDeliveryDateFrom);
        parameters.AddTimestamp("PlannedDeliveryDateTo", request.PlannedDeliveryDateTo);
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

    /// <summary>选单只依赖订单头行；筛选、准确总数和稳定分页在同一SQL快照完成。</summary>
    private static string SearchSql() => $"""
        WITH filtered AS (
            SELECT h."uid" AS "PurchaseOrderId",h."version" AS "Version",
                h."confirmation_status" AS "ConfirmationStatus",h."lifecycle_status" AS "LifecycleStatus",
                h."order_no" AS "OrderNo",h."order_date" AS "OrderDate",h."bo_no" AS "BoNo",
                h."supplier_name" AS "SupplierName",h."demand_source_no" AS "DemandSourceNo",
                (SELECT count(*)::int FROM "public"."purchase_order_line" countLine
                    WHERE countLine."purchase_order_id"=h."uid" AND COALESCE(countLine."is_deleted",FALSE)=FALSE) AS "LineCount",
                {EligibilityColumns}
            FROM "public"."purchase_order_head" h
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
                AND ((@PlannedDeliveryDateFrom IS NULL AND @PlannedDeliveryDateTo IS NULL) OR EXISTS
                    (SELECT 1 FROM "public"."purchase_order_line" plannedLine WHERE plannedLine."purchase_order_id"=h."uid"
                        AND COALESCE(plannedLine."is_deleted",FALSE)=FALSE
                        AND (@PlannedDeliveryDateFrom IS NULL OR plannedLine."planned_delivery_date">=@PlannedDeliveryDateFrom)
                        AND (@PlannedDeliveryDateTo IS NULL OR plannedLine."planned_delivery_date"<@PlannedDeliveryDateTo+INTERVAL '1 day')))
        )
        SELECT (SELECT count(*)::int FROM filtered) AS "TotalCount",
            COALESCE((SELECT jsonb_agg(page ORDER BY page."OrderDate" DESC NULLS LAST,
                page."OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,{UuidOrder("page.\"PurchaseOrderId\"")} DESC) FROM (
                SELECT "PurchaseOrderId","Version","ConfirmationStatus","LifecycleStatus","OrderNo","OrderDate","BoNo","SupplierName","DemandSourceNo","LineCount","CanReportProgress","ReadOnlyReason" FROM filtered facts
                ORDER BY facts."OrderDate" DESC NULLS LAST,facts."OrderNo" COLLATE public.erp_dictionary_ci DESC NULLS LAST,
                    {UuidOrder("facts.\"PurchaseOrderId\"")} DESC LIMIT @PageSize OFFSET @Offset
            ) page),'[]'::jsonb)::text AS "ItemsJson"
        """;

    /// <summary>选单与直达详情共用订单状态资格；不替代调用者权限及提交时版本复验。</summary>
    private static string EligibilityColumns => $"""
        CASE WHEN h."lifecycle_status"={(int)PurchaseOrderLifecycleStatusEnum.Active} AND h."confirmation_status"={(int)ConfirmationStatusEnum.已确认}
            THEN TRUE ELSE FALSE END AS "CanReportProgress",
        CASE WHEN h."lifecycle_status"<>{(int)PurchaseOrderLifecycleStatusEnum.Active} THEN '仅活动采购单允许登记供应商反馈。'
            WHEN h."confirmation_status"<>{(int)ConfirmationStatusEnum.已确认} THEN '仅已确认采购单允许登记供应商反馈。' ELSE '' END AS "ReadOnlyReason"
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
                    "ConfirmationStatuses" or "LifecycleStatuses" => NpgsqlDbType.Array | NpgsqlDbType.Integer,
                    "PlannedDeliveryDateFrom" or "PlannedDeliveryDateTo" => NpgsqlDbType.Timestamp,
                    "Keyword" or "OrderNo" or "SupplierKeyword" or "Sku" => NpgsqlDbType.Text,
                    "LimitConfirmationStatuses" or "LimitLifecycleStatuses" => NpgsqlDbType.Boolean,
                    "Offset" => NpgsqlDbType.Bigint,
                    _ => NpgsqlDbType.Integer
                };
        }
    }
}
