using Dapper;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

/// <summary>物料查询共用的参数化筛选构造，不承载旧计划状态投影。</summary>
public sealed partial class NetDemandRepository
{
    /// <summary>追加包含开始日、包含结束日的日期范围参数。</summary>
    private static void AddDateRange(
        DateTime? from,
        DateTime? to,
        string column,
        string parameterPrefix,
        ICollection<string> where,
        DynamicParameters parameters)
    {
        if (from.HasValue)
        {
            where.Add($"{column}>=@{parameterPrefix}From");
            parameters.Add(parameterPrefix + "From", from.Value);
        }
        if (to.HasValue)
        {
            where.Add($"{column}<@{parameterPrefix}ToExclusive");
            parameters.Add(parameterPrefix + "ToExclusive", to.Value.Date.AddDays(1));
        }
    }

    /// <summary>按非空且去重的值集合追加参数化集合筛选。</summary>
    private static void AddListFilter<T>(
        IEnumerable<T>? values,
        string column,
        string parameterName,
        ICollection<string> where,
        DynamicParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"{column} IN @{parameterName}");
        parameters.Add(parameterName, array);
    }
}
