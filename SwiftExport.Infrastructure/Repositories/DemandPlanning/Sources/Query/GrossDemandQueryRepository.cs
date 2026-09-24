using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;

/// <summary>读取来源需求工作台、分组和详情。</summary>
public sealed class GrossDemandQueryRepository(RepositoryBaseContext context) : IGrossDemandQueryRepository
{
    private IDbConnection Connection => context.Session.Connection;
    private IDbTransaction? Transaction => context.Session.Transaction;

    /// <inheritdoc/>
    public async Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceMode = request.SourceUid.HasValue;
        var pageIndex = sourceMode ? 1 : Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var (where, parameters) = BuildWorkspaceFilters(request);
        if (!sourceMode)
        {
            parameters.Add("Offset", (pageIndex - 1) * pageSize);
            parameters.Add("PageSize", pageSize);
        }

        var whereSql = string.Join(" AND ", where);
        var pagingSql = sourceMode
            ? ";"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var selectSql = $@"
SELECT
    d.[Uid], d.[Version], d.[LifecycleStatus],
    d.[SourceType], d.[SourceUid], d.[SourceNo], d.[SourceLineId], d.[SourceLineNumber], d.[SourceVersion], d.[CustomerId],
    d.[ProductId], d.[IsNonStandard], d.[SkuSnapshot] AS [Sku], d.[LinkSkuSnapshot] AS [LinkSku],
    d.[CustomerSkuSnapshot] AS [CustomerSku], d.[DescriptionSnapshot] AS [Description], d.[ColorSnapshot] AS [Color],
    d.[SpecificationSnapshot] AS [Specification], d.[ImagePathSnapshot] AS [ImagePath], d.[UnitSnapshot] AS [Unit],
    d.[DemandQty], d.[RequiredDate], d.[Remark],
    d.[CreatedDate], d.[CreatedBy], d.[UpdatedDate], d.[UpdatedBy]
FROM [dbo].[GrossDemand] d
WHERE {whereSql}
ORDER BY CASE WHEN d.[RequiredDate] IS NULL THEN 1 ELSE 0 END, d.[RequiredDate], d.[CreatedDate], d.[Uid]
{pagingSql}";
        var countSql = $"SELECT COUNT(1) FROM [dbo].[GrossDemand] d WHERE {whereSql};";

        var items = await Connection.QueryAsync<GrossDemandListItemDto>(
            new CommandDefinition(selectSql, parameters, Transaction, cancellationToken: cancellationToken));
        var total = await Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = sourceMode ? Math.Max(1, total) : pageSize
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var where = new List<string> { "d.[IsDeleted] = 0" };
        var parameters = new DynamicParameters();
        AddListFilter(request.LifecycleStatuses, "d.[LifecycleStatus]", "SourceLifecycleStatuses", where, parameters);
        var sql = $@"
SELECT d.[SourceUid],MAX(d.[SourceType]) AS [SourceType],MAX(d.[SourceNo]) AS [SourceNo],COUNT(1) AS [DemandCount]
FROM [dbo].[GrossDemand] d
WHERE {string.Join(" AND ", where)}
GROUP BY d.[SourceUid]
ORDER BY MAX(d.[SourceType]),MAX(d.[SourceNo]),d.[SourceUid];";
        var groups = await Connection.QueryAsync<GrossDemandSourceGroupDto>(
            new CommandDefinition(sql, parameters, Transaction, cancellationToken: cancellationToken));
        return [.. groups];
    }

    /// <inheritdoc/>
    public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[Uid], d.[Version], d.[LifecycleStatus],
       d.[SourceType], d.[SourceUid], d.[SourceNo], d.[SourceLineId], d.[SourceLineNumber], d.[SourceVersion], d.[CustomerId],
       d.[ProductId], d.[IsNonStandard], d.[SkuSnapshot] AS [Sku], d.[LinkSkuSnapshot] AS [LinkSku],
       d.[CustomerSkuSnapshot] AS [CustomerSku], d.[DescriptionSnapshot] AS [Description], d.[ColorSnapshot] AS [Color],
       d.[SpecificationSnapshot] AS [Specification], d.[ImagePathSnapshot] AS [ImagePath], d.[UnitSnapshot] AS [Unit],
       d.[DemandQty], d.[RequiredDate], d.[Remark],
       d.[LifecycleChangedAt], d.[LifecycleChangedBy], d.[LifecycleChangedReason],
       d.[CreatedDate], d.[CreatedBy], d.[UpdatedDate], d.[UpdatedBy]
FROM [dbo].[GrossDemand] d
WHERE d.[Uid] = @Uid AND d.[IsDeleted] = 0;";
        return Connection.QuerySingleOrDefaultAsync<GrossDemandDetailDto>(
            new CommandDefinition(sql, new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
    }

    private static (List<string> Where, DynamicParameters Parameters) BuildWorkspaceFilters(
        GrossDemandSearchRequest request)
    {
        var where = new List<string> { "d.[IsDeleted] = 0" };
        var parameters = new DynamicParameters();
        AddKeywordFilter(request.Keyword, where, parameters);
        AddListFilter(request.SourceTypes, "d.[SourceType]", "SourceTypes", where, parameters);
        AddListFilter(request.SourceNos, "d.[SourceNo]", "SourceNos", where, parameters);
        AddListFilter(request.Skus, "d.[SkuSnapshot]", "Skus", where, parameters);
        AddListFilter(request.CustomerSkus, "d.[CustomerSkuSnapshot]", "CustomerSkus", where, parameters);
        AddListFilter(request.LifecycleStatuses, "d.[LifecycleStatus]", "LifecycleStatuses", where, parameters);
        if (request.RequiredDateFrom.HasValue)
        {
            where.Add("d.[RequiredDate] >= @RequiredDateFrom");
            parameters.Add("RequiredDateFrom", request.RequiredDateFrom.Value);
        }
        if (request.RequiredDateTo.HasValue)
        {
            where.Add("d.[RequiredDate] < DATEADD(day, 1, @RequiredDateTo)");
            parameters.Add("RequiredDateTo", request.RequiredDateTo.Value.Date);
        }
        if (request.IsNonStandard.HasValue)
        {
            where.Add("d.[IsNonStandard] = @IsNonStandard");
            parameters.Add("IsNonStandard", request.IsNonStandard.Value);
        }
        if (request.SourceUid.HasValue)
        {
            where.Add("d.[SourceUid] = @SourceUid");
            parameters.Add("SourceUid", request.SourceUid.Value);
        }

        return (where, parameters);
    }

    private static void AddKeywordFilter(string? keyword, ICollection<string> where, DynamicParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        where.Add("(d.[SourceNo] LIKE @Keyword OR d.[SkuSnapshot] LIKE @Keyword OR d.[CustomerSkuSnapshot] LIKE @Keyword OR d.[DescriptionSnapshot] LIKE @Keyword)");
        parameters.Add("Keyword", $"%{keyword.Trim()}%");
    }

    private static void AddListFilter<T>(IEnumerable<T>? values, string column, string parameterName,
        ICollection<string> where, DynamicParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"{column} IN @{parameterName}");
        parameters.Add(parameterName, array);
    }

}
