using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;

public sealed class SysDictTypeRepository(RepositoryBaseContext context)
    : DapperRepository<SysDictType>(context), ISysDictTypeRepository
{
    private const string Columns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, DictCode, DictName, IsSystem, Remark, ParentId, SortOrder, TargetViewKey, GroupCode, GroupName";

    public async Task<PagedResponseBase<SysDictTypeDto>> GetPagedAsync(SysDictTypeSearchRequest request)
    {
        request ??= new SysDictTypeSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
        var filters = new List<string> { "(IsDeleted = 0 OR IsDeleted IS NULL)" };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            filters.Add("(DictCode LIKE @Keyword OR DictName LIKE @Keyword OR GroupCode LIKE @Keyword OR GroupName LIKE @Keyword OR TargetViewKey LIKE @Keyword)");
            parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
        }

        var batchKeywords = request.BatchKeywords?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (batchKeywords.Length > 0)
        {
            filters.Add("DictCode IN @BatchKeywords");
            parameters.Add("BatchKeywords", batchKeywords);
        }

        var where = string.Join(" AND ", filters);
        var total = await Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM dbo.SysDictType WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SysDictTypeDto>($@"
            SELECT {Columns}
            FROM dbo.SysDictType
            WHERE {where}
            ORDER BY GroupCode, ISNULL(SortOrder, 2147483647), DictCode, Uid
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", parameters, Transaction);
        return new PagedResponseBase<SysDictTypeDto>
        {
            Items = rows.AsList(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<SysDictType>> GetAllForUpdateAsync()
    {
        var rows = await Connection.QueryAsync<SysDictType>(
            $"SELECT {Columns} FROM dbo.SysDictType WITH (UPDLOCK, ROWLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL",
            transaction: Transaction);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SysDictType>> GetByDictCodesAsync(
        IEnumerable<string> dictCodes,
        bool withUpdLock = false)
    {
        var codes = dictCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length == 0) return [];
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var rows = await Connection.QueryAsync<SysDictType>(
            $"SELECT {Columns} FROM dbo.SysDictType {lockHint} WHERE DictCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)",
            new { Codes = codes }, Transaction);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> GetReferencedTargetViewKeysAsync(IEnumerable<string> targetViewKeys)
    {
        var keys = targetViewKeys.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length == 0) return [];
        var rows = await Connection.QueryAsync<string>(@"
            SELECT DISTINCT TargetViewKey
            FROM dbo.SysDictType WITH (UPDLOCK, ROWLOCK)
            WHERE TargetViewKey IN @Keys AND (IsDeleted = 0 OR IsDeleted IS NULL);",
            new { Keys = keys }, Transaction);
        return rows.AsList();
    }
}
