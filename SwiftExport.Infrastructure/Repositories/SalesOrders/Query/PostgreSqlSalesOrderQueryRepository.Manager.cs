using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

/// <summary>销售管理只查询订单事实和生命周期；数量、单位与下游来源及交付状态解耦。</summary>
public sealed partial class PostgreSqlSalesOrderQueryRepository
{
    /// <summary>按订单事实筛选并稳定分页；筛选明细仅决定选单，汇总始终使用整单有效行。</summary>
    public async Task<PagedResponseBase<SalesOrderManagerListItemDto>> SearchManagerAsync(
        SalesOrderManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var where = CreateManagerWhere(request, parameters);
        var from = "FROM public.sales_order_header h LEFT JOIN public.customer c ON c.uid=h.customer_id AND c.is_deleted=FALSE";
        var count = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*)::integer {from} {where}", new ManagerParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await context.Session.Connection.QueryAsync<SalesOrderManagerListItemDto>(new CommandDefinition($"""
            SELECT h.uid AS "Uid",h.version AS "Version",h.document_status AS "DocumentStatus",h.confirmation_status AS "ConfirmationStatus",
                h.order_no AS "OrderNo",COALESCE(h.customer_id,'00000000-0000-0000-0000-000000000000'::uuid) AS "CustomerId",
                COALESCE(h.customer_code,'') AS "CustomerCode",COALESCE(c.customer_name,'') AS "CustomerName",
                COALESCE(h.customer_po_no,'') AS "CustomerPoNo",COALESCE(h.invoice_no,'') AS "InvoiceNo",
                COALESCE(h.currency,'') AS "Currency",h.exchange_rate AS "ExchangeRate",
                CASE WHEN facts.line_count=0 THEN 0::numeric
                     WHEN facts.unit_count=1 AND facts.missing_unit_count=0 THEN facts.ordered_qty END AS "TotalQty",
                CASE WHEN facts.unit_count=1 AND facts.missing_unit_count=0 THEN facts.unit_snapshot END AS "UnitSnapshot",
                (facts.unit_count>1) AS "HasMixedUnits",(facts.missing_unit_count>0) AS "HasMissingUnit",
                COALESCE(h.total_amount_original_currency,0) AS "TotalAmountOriginalCurrency",
                COALESCE(h.total_amount_local_currency,0) AS "TotalAmountLocalCurrency",
                h.order_date AS "OrderDate",h.delivery_date AS "DeliveryDate",h.reply_date AS "ReplyDate",h.created_date AS "CreatedDate",
                COALESCE(h.created_by,'') AS "CreatedBy",h.updated_date AS "UpdatedDate",COALESCE(h.updated_by,'') AS "UpdatedBy"
            {from}
            LEFT JOIN LATERAL (
                SELECT count(*)::integer AS line_count,sum(l.qty) AS ordered_qty,
                    count(DISTINCT NULLIF(rtrim(l.unit_snapshot,' '),'') COLLATE public.erp_dictionary_ci)::integer AS unit_count,
                    count(*) FILTER (WHERE NULLIF(rtrim(l.unit_snapshot,' '),'') IS NULL)::integer AS missing_unit_count,
                    max(NULLIF(rtrim(l.unit_snapshot,' '),'') COLLATE public.erp_dictionary_ci) AS unit_snapshot
                FROM public.sales_order_line l WHERE l.sales_order_id=h.uid AND l.is_deleted=FALSE
            ) facts ON TRUE
            {where}
            ORDER BY h.created_date DESC NULLS LAST,{SourceUuidOrder("h.uid")} DESC LIMIT @PageSize OFFSET @Offset
            """, new ManagerParameters(parameters), context.Session.Transaction, cancellationToken: cancellationToken));
        return new() { TotalCount = count, Items = rows.AsList(), PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>显式绑定状态数组，兼容测试包装连接；不扩展公共参数包。</summary>
    private sealed class ManagerParameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>仅配置本命令克隆参数的数组类型，原参数、会话和事务不变。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                if (parameter.ParameterName is "DocumentStatuses" or "ConfirmationStatuses")
                    parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer;
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
            where.Append($" AND EXISTS (SELECT 1 FROM \"public\".\"sales_order_line\" sku_line WHERE sku_line.sales_order_id=h.uid"
                + " AND sku_line.is_deleted=FALSE AND rtrim(sku_line.customer_sku,' ') COLLATE public.erp_dictionary_ci=ANY(@CustomerSkus))");
            parameters.AddTexts("CustomerSkus", skus);
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var headColumns = new[] { "h.order_no", "h.customer_code", "h.customer_po_no", "h.invoice_no", "c.customer_name" };
            var lineColumns = new[] { "keyword_line.sku", "keyword_line.customer_sku", "keyword_line.link_sku", "keyword_line.description_en" };
            where.Append(" AND (" + string.Join(" OR ", headColumns.Select(x => ManagerLike(x, "Keyword")))
                + $" OR EXISTS (SELECT 1 FROM \"public\".\"sales_order_line\" keyword_line WHERE keyword_line.sales_order_id=h.uid"
                + " AND keyword_line.is_deleted=FALSE AND (" + string.Join(" OR ", lineColumns.Select(x => ManagerLike(x, "Keyword"))) + ")))");
            parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
        }
        if (request.EffectiveDeliveryDateFrom.HasValue || request.EffectiveDeliveryDateTo.HasValue)
        {
            where.Append($" AND EXISTS (SELECT 1 FROM \"public\".\"sales_order_line\" due_line WHERE due_line.sales_order_id=h.uid AND due_line.is_deleted=FALSE"
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

}
