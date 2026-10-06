using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>销售Manager按候选、行事实、冻结汇总和当前商业页分别观察；不接完整接口或运行DI。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository
{
    /// <summary>按源筛选与业务日读取销售管理页；独立总数保留超末页，借用可空事务且不刷新已冻结事实。</summary>
    public async Task<PagedResponseBase<SalesOrderManagerListItemDto>> SearchManagerAsync(
        SalesOrderManagerSearchRequest request, DateTime businessToday, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = CreateParameters(Guid.Empty);
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        parameters.Add("QuickView", (int)request.QuickView);
        parameters.AddTimestamp("Today", businessToday.Date);
        parameters.AddTimestamp("SoonEnd", businessToday.Date.AddDays(7));
        var where = CreateManagerWhere(request, parameters);
        var candidates = await ReadManagerAsync<CandidateOrder>($"""
            SELECT h.uid AS "Uid",h.version AS "Version",h.document_status AS "DocumentStatus",
                   h.confirmation_status AS "ConfirmationStatus",h.customer_id AS "CustomerId",
                   h.order_no AS "OrderNo",h.delivery_date AS "DeliveryDate"
            FROM {Headers} h
            LEFT JOIN {Table(typeof(Customer))} c ON c.uid=h.customer_id AND c.is_deleted=FALSE
            {where}
            """, parameters, cancellationToken);
        parameters.Add("Candidates", JsonSerializer.Serialize(candidates));
        await FreezeLineFactsAsync(parameters, cancellationToken);
        // 汇总、过滤及count只观察冻结关系，可以同一SQL完成；当前真实头页仍是下一独立阶段。
        var frozen = (await ReadManagerAsync<ManagerFrozenFacts>(CreateManagerFactsSql(), parameters, cancellationToken)).Single();
        parameters.Add("OrderFacts", frozen.FilteredFactsJson);
        var items = await ReadManagerAsync<SalesOrderManagerListItemDto>(CreateManagerPageSql(), parameters, cancellationToken);
        return new() { TotalCount = frozen.TotalCount, Items = items, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>Manager专属参数包在执行前明确数组/JSON类型，不修改只读行事实helper或公共参数实现。</summary>
    private async Task<List<T>> ReadManagerAsync<T>(string sql, PostgreSqlParameters parameters, CancellationToken token)
        => (await context.Session.Connection.QueryAsync<T>(new CommandDefinition(sql,
            new ManagerParameters(parameters), context.Session.Transaction, cancellationToken: token))).ToList();

    /// <summary>复用既有参数克隆，再明确Manager新增数组和冻结JSON类型；不让Dapper依包装连接猜列表。</summary>
    private sealed class ManagerParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>仅调整本命令克隆参数的类型；原包、共享方法、事务及值保持不变。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
            {
                if (parameter.ParameterName is "DocumentStatuses" or "ConfirmationStatuses")
                    parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer;
                if (parameter.ParameterName is "Candidates" or "LineFacts" or "OrderFacts")
                    parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            }
        }
    }

    /// <summary>只在候选阶段筛选真实头和有效行；搜索文本不Trim，列表集合按源去空白及忽略大小写去重。</summary>
    private static string CreateManagerWhere(SalesOrderManagerSearchRequest request, PostgreSqlParameters parameters)
    {
        var where = new StringBuilder("WHERE h.is_deleted=FALSE");
        var codes = request.CustomerCodes?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (codes.Length > 0)
        {
            where.Append(" AND rtrim(h.customer_code,' ') COLLATE public.erp_dictionary_ci=ANY(@CustomerCodes)");
            parameters.AddTexts("CustomerCodes", codes);
        }
        AddManagerLike(where, parameters, "h.order_no", "OrderNo", request.OrderNo);
        AddManagerLike(where, parameters, "h.customer_po_no", "CustomerPoNo", request.CustomerPoNo);
        AddManagerLike(where, parameters, "h.invoice_no", "InvoiceNo", request.InvoiceNo);
        if (request.CustomerId is { } customerId && customerId != Guid.Empty)
        {
            where.Append(" AND h.customer_id=@CustomerId");
            parameters.Add("CustomerId", customerId);
        }
        if (request.OrderDateFrom.HasValue)
        {
            where.Append(" AND h.order_date>=@OrderDateFrom");
            parameters.AddTimestamp("OrderDateFrom", request.OrderDateFrom.Value.Date);
        }
        if (request.OrderDateTo.HasValue)
        {
            where.Append(" AND h.order_date<@OrderDateTo");
            parameters.AddTimestamp("OrderDateTo", request.OrderDateTo.Value.Date.AddDays(1));
        }
        if (request.DocumentStatuses?.Count > 0)
        {
            where.Append(" AND h.document_status=ANY(@DocumentStatuses)");
            // IDynamicParameters内部的NpgsqlParameter绑定整型数组，避免包装连接触发Dapper列表展开。
            parameters.Add("DocumentStatuses", request.DocumentStatuses.Distinct().Select(x => (int)x).ToArray());
        }
        if (request.ConfirmationStatuses?.Count > 0)
        {
            where.Append(" AND h.confirmation_status=ANY(@ConfirmationStatuses)");
            parameters.Add("ConfirmationStatuses", request.ConfirmationStatuses.Distinct().Select(x => (int)x).ToArray());
        }
        var skus = request.CustomerSkus?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (skus.Length > 0)
        {
            where.Append($" AND EXISTS (SELECT 1 FROM {Lines} sku_line WHERE sku_line.sales_order_id=h.uid"
                + " AND sku_line.is_deleted=FALSE AND rtrim(sku_line.customer_sku,' ') COLLATE public.erp_dictionary_ci=ANY(@CustomerSkus))");
            parameters.AddTexts("CustomerSkus", skus);
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var headColumns = new[] { "h.order_no", "h.customer_code", "h.customer_po_no", "h.invoice_no", "c.customer_name" };
            var lineColumns = new[] { "keyword_line.sku", "keyword_line.customer_sku", "keyword_line.link_sku", "keyword_line.description_en" };
            where.Append(" AND (" + string.Join(" OR ", headColumns.Select(x => ManagerLike(x, "Keyword")))
                + $" OR EXISTS (SELECT 1 FROM {Lines} keyword_line WHERE keyword_line.sales_order_id=h.uid"
                + " AND keyword_line.is_deleted=FALSE AND (" + string.Join(" OR ", lineColumns.Select(x => ManagerLike(x, "Keyword"))) + ")))");
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        if (request.EffectiveDeliveryDateFrom.HasValue || request.EffectiveDeliveryDateTo.HasValue)
        {
            where.Append($" AND EXISTS (SELECT 1 FROM {Lines} due_line WHERE due_line.sales_order_id=h.uid AND due_line.is_deleted=FALSE"
                + " AND (@EffectiveDueFrom IS NULL OR COALESCE(due_line.expected_delivery_date,h.delivery_date)>=@EffectiveDueFrom)"
                + " AND (@EffectiveDueTo IS NULL OR COALESCE(due_line.expected_delivery_date,h.delivery_date)<@EffectiveDueTo))");
        }
        parameters.AddTimestamp("EffectiveDueFrom", request.EffectiveDeliveryDateFrom?.Date);
        parameters.AddTimestamp("EffectiveDueTo", request.EffectiveDeliveryDateTo?.Date.AddDays(1));
        return where.ToString();
    }

    /// <summary>仅调用源限定字面模式构造器；空白输入忽略，空格和问号保留原通配语义。</summary>
    private static void AddManagerLike(StringBuilder where, PostgreSqlParameters parameters, string column, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        where.Append(" AND " + ManagerLike(column, name));
        parameters.Add(name, SqlLikePattern.Build(value));
    }

    /// <summary>固定内部列在既有字典比较下匹配参数；不接受外部SQL或扩展原始LIKE字符类解析。</summary>
    private static string ManagerLike(string column, string parameter) => $"{column} COLLATE public.erp_dictionary_ci LIKE @{parameter} ESCAPE '~'";

    /// <summary>只对已缓冲七字段与九字段关系做源聚合；COUNT/SUM空值与无行LEFT JOIN边界保持SQL执行。</summary>
    private static string CreateManagerFactsSql() => $"""
        WITH candidate AS ({CandidateRelation}),line_facts AS ({LineFactsRelation}),order_facts AS (
            SELECT h.uid,
                   COUNT(l."SalesOrderLineId")::integer AS line_count,
                   SUM(CASE WHEN l."ReviewReason" IS NOT NULL THEN 1 ELSE 0 END)::integer AS review_count,
                   SUM(CASE WHEN l."SalesOrderLineId" IS NOT NULL AND l."ShippedQty" IS NULL THEN 1 ELSE 0 END)::integer AS unknown_count,
                   COUNT(DISTINCT NULLIF(rtrim(l."UnitSnapshot",' '),'') COLLATE public.erp_dictionary_ci)::integer AS unit_count,
                   SUM(CASE WHEN NULLIF(rtrim(l."UnitSnapshot",' '),'') IS NULL THEN 1 ELSE 0 END)::integer AS missing_unit_count,
                   SUM(CASE WHEN l."ShippedQty" IS NOT NULL AND l."UnshippedQty">0 THEN 1 ELSE 0 END)::integer AS outstanding_line_count,
                   SUM(CASE WHEN l."ShippedQty" IS NOT NULL AND l."UnshippedQty">0
                                 AND h.document_status=@OrderOpen AND h.confirmation_status=@Confirmed
                                 AND l."EffectiveDueDate"<@Today THEN 1 ELSE 0 END)::integer AS overdue_line_count,
                   SUM(CASE WHEN l."ShippedQty" IS NOT NULL AND l."UnshippedQty">0
                                 AND h.document_status=@OrderOpen AND h.confirmation_status=@Confirmed
                                 AND l."EffectiveDueDate">=@Today AND l."EffectiveDueDate"<=@SoonEnd THEN 1 ELSE 0 END)::integer AS due_soon_line_count,
                   SUM(CASE WHEN h.confirmation_status=@Pending AND l."UnshippedQty">0
                                 AND l."EffectiveDueDate"<@Today THEN 1 ELSE 0 END)::integer AS pending_past_due_line_count,
                   MIN(CASE WHEN l."ShippedQty" IS NULL OR l."UnshippedQty">0 THEN l."EffectiveDueDate" END) AS earliest_open_due_date,
                   BOOL_OR(CASE WHEN l."SalesOrderLineId" IS NOT NULL AND (l."ShippedQty" IS NULL OR l."UnshippedQty">0)
                                AND l."EffectiveDueDate" IS NULL THEN TRUE ELSE FALSE END) AS has_undated_open_line,
                   MIN(CASE WHEN l."ShippedQty" IS NOT NULL AND l."UnshippedQty">0
                                 AND h.document_status=@OrderOpen AND h.confirmation_status=@Confirmed AND l."EffectiveDueDate"<@Today
                            THEN l."EffectiveDueDate" END) AS earliest_overdue_date,
                   MIN(CASE WHEN l."ShippedQty" IS NOT NULL AND l."UnshippedQty">0
                                 AND h.document_status=@OrderOpen AND h.confirmation_status=@Confirmed
                                 AND l."EffectiveDueDate">=@Today AND l."EffectiveDueDate"<=@SoonEnd THEN l."EffectiveDueDate" END) AS earliest_due_soon_date,
                   SUM(l."Qty") AS ordered_qty,SUM(l."ShippedQty") AS shipped_qty,SUM(l."UnshippedQty") AS unshipped_qty,
                   MAX(l."UnitSnapshot" COLLATE public.erp_dictionary_ci) AS unit_snapshot
            FROM candidate h LEFT JOIN line_facts l ON l."SalesOrderId"=h.uid GROUP BY h.uid
        ),filtered AS MATERIALIZED (
            SELECT f.* FROM order_facts f JOIN candidate h ON h.uid=f.uid WHERE
                (@QuickView=0 OR (@QuickView=1 AND h.confirmation_status=@Pending)
                 OR (@QuickView=2 AND f.line_count>0 AND f.review_count=0 AND f.shipped_qty=0 AND f.outstanding_line_count>0)
                 OR (@QuickView=3 AND f.line_count>0 AND f.review_count=0 AND f.shipped_qty>0 AND f.outstanding_line_count>0)
                 OR (@QuickView=4 AND f.line_count>0 AND f.review_count=0 AND f.outstanding_line_count=0)
                 OR (@QuickView=5 AND f.due_soon_line_count>0) OR (@QuickView=6 AND f.overdue_line_count>0)
                 OR (@QuickView=7 AND h.document_status=@OrderClosed AND f.outstanding_line_count>0)
                 OR (@QuickView=8 AND (f.review_count>0 OR f.line_count=0)))
        )
        SELECT (SELECT COUNT(*)::integer FROM filtered) AS "TotalCount",
               COALESCE((SELECT jsonb_agg(to_jsonb(f)) FROM filtered f),'[]'::jsonb)::text AS "FilteredFactsJson"
        """;

    /// <summary>页按冻结事实身份重新读取当前真实头/客户；不再次限制头软删除，不把总量门槛改成ReviewCount=0。</summary>
    private static string CreateManagerPageSql() => $"""
        WITH order_facts AS ({ManagerOrderFactsRelation}),line_facts AS ({LineFactsRelation})
        SELECT h.uid AS "Uid",h.version AS "Version",h.document_status AS "DocumentStatus",h.confirmation_status AS "ConfirmationStatus",
               CASE WHEN f.line_count=0 OR f.review_count>0 THEN NULL::integer
                    WHEN f.outstanding_line_count=0 THEN 2 WHEN f.shipped_qty=0 THEN 0 ELSE 1 END AS "FulfillmentStatus",
               h.order_no AS "OrderNo",COALESCE(h.customer_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "CustomerId",
               COALESCE(h.customer_code,'') AS "CustomerCode",COALESCE(c.customer_name,'') AS "CustomerName",
               COALESCE(h.customer_po_no,'') AS "CustomerPoNo",COALESCE(h.invoice_no,'') AS "InvoiceNo",
               COALESCE(h.currency,'') AS "Currency",COALESCE(h.exchange_rate,0) AS "ExchangeRate",
               CASE WHEN {ManagerQuantityGate} THEN f.ordered_qty END AS "TotalQty",
               CASE WHEN {ManagerQuantityGate} THEN f.shipped_qty END AS "TotalShippedQty",
               CASE WHEN {ManagerQuantityGate} THEN f.unshipped_qty END AS "TotalUnshippedQty",
               CASE WHEN {ManagerQuantityGate} THEN f.unit_snapshot END AS "UnitSnapshot",
               f.earliest_open_due_date AS "EarliestOpenDueDate",f.has_undated_open_line AS "HasUndatedOpenLine",
               f.overdue_line_count AS "OverdueLineCount",f.due_soon_line_count AS "DueSoonLineCount",
               f.earliest_overdue_date AS "EarliestOverdueDate",f.earliest_due_soon_date AS "EarliestDueSoonDate",
               f.pending_past_due_line_count AS "PendingPastDueLineCount",(f.line_count=0 OR f.review_count>0) AS "NeedsReview",
               CASE WHEN f.line_count=0 THEN '无有效销售行' ELSE COALESCE(reasons.review_reason,'') END AS "ReviewReason",
               COALESCE(h.total_amount_original_currency,0) AS "TotalAmountOriginalCurrency",
               COALESCE(h.total_amount_local_currency,0) AS "TotalAmountLocalCurrency",
               h.order_date AS "OrderDate",h.delivery_date AS "DeliveryDate",h.reply_date AS "ReplyDate",h.created_date AS "CreatedDate",
               COALESCE(h.created_by,'') AS "CreatedBy",h.updated_date AS "UpdatedDate",COALESCE(h.updated_by,'') AS "UpdatedBy"
        FROM order_facts f JOIN {Headers} h ON h.uid=f.uid
        LEFT JOIN {Table(typeof(Customer))} c ON c.uid=h.customer_id AND c.is_deleted=FALSE
        LEFT JOIN LATERAL (
            SELECT string_agg(distinct_reasons.reason,'、' ORDER BY distinct_reasons.reason COLLATE public.erp_dictionary_ci) AS review_reason
            FROM (SELECT DISTINCT lf."ReviewReason" COLLATE public.erp_dictionary_ci AS reason FROM line_facts lf
                  WHERE lf."SalesOrderId"=f.uid AND lf."ReviewReason" IS NOT NULL) distinct_reasons
        ) reasons ON TRUE
        ORDER BY CASE WHEN @QuickView=6 THEN f.earliest_overdue_date WHEN @QuickView=5 THEN f.earliest_due_soon_date END ASC NULLS FIRST,
                 h.created_date DESC NULLS LAST,{SourceUuidOrder("h.uid")} DESC LIMIT @PageSize OFFSET @Offset
        """;

    // 原头总量只看有行/无未知/同单位/无缺单位；超发原因不会清掉仍确定的12/0。
    private const string ManagerQuantityGate = "f.line_count>0 AND f.unknown_count=0 AND f.unit_count=1 AND f.missing_unit_count=0";

    /// <summary>局部汇总结果只承载独立总数和冻结JSON，C#不重算业务数量或QuickView。</summary>
    private sealed class ManagerFrozenFacts
    {
        public int TotalCount { get; set; }
        public string FilteredFactsJson { get; set; } = "[]";
    }

    // 记录集类型沿源int/decimal/date/bit边界，避免JSON重读时将SQL NULL置零。
    private const string ManagerOrderFactsRelation = """
        SELECT * FROM jsonb_to_recordset(CAST(@OrderFacts AS jsonb)) AS f(
            uid uuid,line_count integer,review_count integer,unknown_count integer,unit_count integer,missing_unit_count integer,
            outstanding_line_count integer,overdue_line_count integer,due_soon_line_count integer,pending_past_due_line_count integer,
            earliest_open_due_date timestamp without time zone,has_undated_open_line boolean,
            earliest_overdue_date timestamp without time zone,earliest_due_soon_date timestamp without time zone,
            ordered_qty numeric,shipped_qty numeric,unshipped_qty numeric,unit_snapshot text)
        """;
}
