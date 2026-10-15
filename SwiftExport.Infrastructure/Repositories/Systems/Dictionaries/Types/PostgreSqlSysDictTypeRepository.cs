using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using Dapper;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;
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
    public async Task<IReadOnlyList<SysDictType>> GetAllIncludingDeletedAsync(bool withUpdLock = false, CancellationToken cancellationToken = default)
    {
        var lockHint = GetLockHint(withUpdLock);
        var rows = await Connection.QueryAsync<SysDictType>(new CommandDefinition(
            $"SELECT {Columns} FROM public.sys_dict_type ORDER BY uid{lockHint}", transaction: Transaction,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>事务级锁只协调显式同步；固定命名键覆盖空库首次插入的竞争，提交或回滚时自动释放。</summary>
    public async Task LockBuiltInSyncAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null) throw new InvalidOperationException("内置字典同步必须位于应用层事务中。");
        await Connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended('SwiftExport.BuiltInDictionarySync', 0));",
            transaction: Transaction, cancellationToken: cancellationToken));
    }

    /// <summary>首次落库保留目录身份；已有类型只修复标记，不覆盖用户配置，也不重建业务引用。</summary>
    public async Task<RepositoryWriteResult> SaveBuiltInAsync(SysDictType entity, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (Transaction is null) throw new InvalidOperationException("内置字典同步必须位于应用层事务中。");
        if (entity.Uid == Guid.Empty || expectedVersion < 0)
            throw new ArgumentException("内置字典身份或预期版本无效。");
        entity.IsDeleted = false;
        entity.IsSystem = true;
        if (expectedVersion == 0)
        {
            entity.Version = 1;
            AuditStamp.StampForInsert(entity);
            // 不走通用新增：通用新增会分配新 Guid，而内置身份必须与代码回退及既有引用保持一致。
            var rows = await Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.sys_dict_type
                    (uid, version, is_deleted, created_date, created_by, updated_date, updated_by,
                     dict_code, dict_name, is_system, remark, parent_id, sort_order, group_code, group_name)
                SELECT @Uid, @Version, @IsDeleted, @CreatedDate, @CreatedBy, @UpdatedDate, @UpdatedBy,
                       @DictCode, @DictName, @IsSystem, @Remark, @ParentId, @SortOrder, @GroupCode, @GroupName
                WHERE NOT EXISTS (
                    SELECT 1 FROM public.sys_dict_type
                    WHERE lower(btrim(dict_code, ' ')) = lower(btrim(@DictCode, ' ')))
                ON CONFLICT DO NOTHING;
                """, PostgreSqlParameters.Entity(entity, Metadata), Transaction, cancellationToken: cancellationToken));
            return rows == 1
                ? RepositoryWriteResult.Ok(entity.Uid, 0, 1)
                : RepositoryWriteResult.Fail(entity.Uid, 0, ServiceErrorCodes.ConcurrencyConflict, "内置字典类型已变化，请重新同步。");
        }

        AuditStamp.StampForUpdate(entity);
        var parameters = PostgreSqlParameters.Entity(entity, Metadata);
        parameters.Add("ExpectedVersion", expectedVersion);
        var version = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            UPDATE public.sys_dict_type SET is_deleted=FALSE, is_system=TRUE,
                updated_date=@UpdatedDate, updated_by=@UpdatedBy, version=version+1
            WHERE uid=@Uid AND version=@ExpectedVersion RETURNING version;
            """, parameters, Transaction, cancellationToken: cancellationToken));
        if (version is null)
            return RepositoryWriteResult.Fail(entity.Uid, expectedVersion, ServiceErrorCodes.ConcurrencyConflict, "内置字典类型已变化，请重新同步。");
        entity.Version = version.Value;
        return RepositoryWriteResult.Ok(entity.Uid, expectedVersion, version.Value);
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
