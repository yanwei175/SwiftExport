using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using Dapper;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;

/// <summary>读取和保存 PostgreSQL 字典类型持久事实，复用当前请求会话和事务。</summary>
public sealed class PostgreSqlSysDictTypeRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<SysDictType>(context), ISysDictTypeRepository
{
    private const string Columns = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "dict_code" AS "DictCode",
        "dict_name" AS "DictName",
        "is_system" AS "IsSystem",
        "remark" AS "Remark",
        "parent_id" AS "ParentId",
        "sort_order" AS "SortOrder",
        "group_code" AS "GroupCode",
        "group_name" AS "GroupName"
    """;

    /// <summary>读取全部持久候选，供应用层保留软删除内置类型的身份；查询不修改原记录。</summary>
    public async Task<IReadOnlyList<SysDictType>> GetAllIncludingDeletedAsync(bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var rows = await Connection.QueryAsync<SysDictType>(
            $"SELECT {Columns} FROM public.sys_dict_type ORDER BY uid{lockHint}", transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>在共享会话内查询数据库活动类型分页；内置合并由应用层处理。</summary>
    public async Task<PagedResponseBase<SysDictTypeDto>> GetPagedAsync(SysDictTypeSearchRequest request)
    {
        request ??= new SysDictTypeSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var parameters = new PostgreSqlParameters();
        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var filters = new List<string> { "(is_deleted = FALSE OR is_deleted IS NULL)" };
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            filters.Add("(dict_code LIKE @Keyword ESCAPE '' OR dict_name LIKE @Keyword ESCAPE '' OR group_code LIKE @Keyword ESCAPE '' OR group_name LIKE @Keyword ESCAPE '')");
            parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
        }

        var batchKeywords = request.BatchKeywords?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (batchKeywords.Length > 0)
        {
            filters.Add("rtrim(dict_code, ' ') = ANY(@BatchKeywords)");
            parameters.AddTexts("BatchKeywords", batchKeywords);
        }

        var where = string.Join(" AND ", filters);
        var total = await Connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM public.sys_dict_type WHERE {where}", parameters, Transaction);
        var rows = await Connection.QueryAsync<SysDictTypeDto>($@"
            SELECT {Columns}
            FROM public.sys_dict_type
            WHERE {where}
            ORDER BY group_code NULLS FIRST, COALESCE(sort_order, 2147483647), dict_code, uid
            LIMIT @PageSize OFFSET @Offset;", parameters, Transaction);
        return new PagedResponseBase<SysDictTypeDto>
        {
            Items = rows.AsList(),
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    /// <summary>在共享会话内执行PostgreSQL字典查询，保持分页、软删除及调用方事务边界。</summary>
    public async Task<IReadOnlyList<SysDictType>> GetAllForUpdateAsync()
    {
        var lockHint = GetLockHint(true);
        var rows = await Connection.QueryAsync<SysDictType>(
            $"SELECT {Columns} FROM public.sys_dict_type WHERE is_deleted = FALSE OR is_deleted IS NULL ORDER BY uid{lockHint}",
            transaction: Transaction);
        return rows.AsList();
    }

    /// <summary>在共享会话内执行PostgreSQL字典查询，保持分页、软删除及调用方事务边界。</summary>
    public async Task<IReadOnlyList<SysDictType>> GetByDictCodesAsync(
        IEnumerable<string> dictCodes,
        bool withUpdLock = false)
    {
        var codes = dictCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length == 0) return [];
        var lockHint = GetLockHint(withUpdLock);
        var rows = await Connection.QueryAsync<SysDictType>(
            $"SELECT {Columns} FROM public.sys_dict_type WHERE rtrim(dict_code, ' ') = ANY(@Codes) AND (is_deleted = FALSE OR is_deleted IS NULL) ORDER BY uid{lockHint}",
            CodeParameters(codes), Transaction);
        return rows.AsList();
    }

    /// <summary>绑定编码集合，避免Dapper把文本数组展开为SQL Server形式的IN参数。</summary>
    private static PostgreSqlParameters CodeParameters(IEnumerable<string> codes)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTexts("Codes", codes);
        return parameters;
    }
}
