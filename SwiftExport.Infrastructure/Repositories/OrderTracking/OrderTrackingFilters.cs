using System.Text;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.OrderTracking;

/// <summary>来源搜索与组内匹配共用基础筛选，避免计数和命中范围分叉。</summary>
internal static class OrderTrackingFilters
{
    internal const string GroupId = "COALESCE(NULLIF(g.source_uid,'00000000-0000-0000-0000-000000000000'::uuid),g.uid)";
    internal const string GroupType = "upper(rtrim(g.source_type,' ')) COLLATE public.erp_dictionary_ci";

    /// <summary>只构造基础事实筛选及显式参数，不读取执行区域。</summary>
    internal static (string Where, PostgreSqlParameters Parameters) Build(OrderTrackingSourceSearchRequest request)
        =>Build(request.SourceTypes,request.CustomerCodes,request.Keyword,request.LifecycleStatuses,request.RequiredDateFrom,request.RequiredDateTo);

    /// <summary>独立匹配请求复用同一行基础筛选，不引入分页参数。</summary>
    internal static (string Where, PostgreSqlParameters Parameters) Build(OrderTrackingSourceMatchRequest request)
        =>Build(request.SourceTypes,request.CustomerCodes,request.Keyword,request.LifecycleStatuses,request.RequiredDateFrom,request.RequiredDateTo);

    /// <summary>来源搜索和匹配身份查询共享字段、字面关键词及按日筛选口径。</summary>
    private static (string Where, PostgreSqlParameters Parameters) Build(IEnumerable<string>? sourceTypes,IEnumerable<string>? customerCodes,string? keyword,IEnumerable<GrossDemandLifecycleStatusEnum>? lifecycleStatuses,DateTime? requiredDateFrom,DateTime? requiredDateTo)
    {
        var parameters = new PostgreSqlParameters();
        var where = new StringBuilder("WHERE " + OrderTrackingFactsSql.Eligible);
        var sources = sourceTypes?.Distinct().ToArray() ?? ["SO"];
        if (sources.Length > 0)
        {
            where.Append(" AND rtrim(g.source_type,' ') COLLATE public.erp_dictionary_ci=ANY(@SourceTypes)");
            parameters.AddTexts("SourceTypes", sources);
        }
        var codes = customerCodes?.Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (codes.Length > 0)
        {
            where.Append(" AND rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci=ANY(@CustomerCodes)");
            parameters.AddTexts("CustomerCodes", codes);
        }
        AddLike(where, parameters, ["g.source_no", "so.customer_po_no", "so.invoice_no",
            "g.sku_snapshot", "g.customer_sku_snapshot", "g.link_sku_snapshot"], "Keyword", keyword);
        var statuses = lifecycleStatuses?.Distinct().Select(status => (int)status).ToArray() ?? [];
        if (statuses.Length > 0)
        {
            where.Append(" AND g.lifecycle_status=ANY(@LifecycleStatuses)");
            parameters.Add("LifecycleStatuses", statuses);
        }
        if (requiredDateFrom.HasValue)
        {
            where.Append(" AND g.required_date>=@RequiredDateFrom");
            parameters.AddTimestamp("RequiredDateFrom", requiredDateFrom.Value.Date);
        }
        if (requiredDateTo.HasValue)
        {
            // 在 PG 内推进一天，允许 .NET 最大日期作为包含式上界，不在应用端溢出。
            where.Append(" AND g.required_date<@RequiredDateTo + INTERVAL '1 day'");
            parameters.AddTimestamp("RequiredDateTo", requiredDateTo.Value.Date);
        }
        return (where.ToString(), parameters);
    }
    /// <summary>仅将本用例的生命周期集合显式绑定为整型数组，不扩大公共参数包。</summary>
    internal sealed class Parameters(PostgreSqlParameters parameters) : SqlMapper.IDynamicParameters
    {
        /// <summary>每条命令使用独立参数实例，支持调用方包装连接。</summary>
        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            foreach (NpgsqlParameter parameter in command.Parameters)
                if (parameter.ParameterName == "LifecycleStatuses")
                    parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer;
        }
    }

    /// <summary>字面包含匹配只转义 PG 通配符与转义字符，空格和问号保持字面含义。</summary>
    private static void AddLike(StringBuilder where, PostgreSqlParameters parameters, string[] columns, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        where.Append(" AND (" + string.Join(" OR ", columns.Select(column => $"{column} COLLATE public.erp_dictionary_ci LIKE @{name} ESCAPE '~'")) + ")");
        parameters.Add(name, "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%");
    }
}
