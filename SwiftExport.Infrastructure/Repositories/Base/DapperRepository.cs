using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Common;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>
/// 轻量 Dapper 泛型仓储基类。
/// 只承载单实体 CRUD 和基础按 Uid 查询；复杂批量、动态查询、分页查询应下沉到具体业务仓储。
/// </summary>
public class DapperRepository<T>(RepositoryBaseContext context) : IRepository<T> where T : BaseEntity
{
    private readonly DbSession _session = context.Session ?? throw new ArgumentNullException(nameof(context.Session));
    private readonly IAuditStampService _auditStamp = context.AuditStamp;
    private ILogger? _loggerInstance;

    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());
    protected IDbConnection Connection => _session.Connection;
    protected IDbTransaction? Transaction => _session.Transaction;
    protected IAuditStampService AuditStamp => _auditStamp;

    protected static readonly EntityMetadata Metadata = EntityMetadataCache.GetMetadata<T>();

    protected static readonly string TableName = Metadata.TableName;




    protected virtual IReadOnlyList<string> WriteableProperties => WriteablePropertyCache.Value;
    protected virtual IReadOnlyList<string> InsertProperties => WriteableProperties;
    protected virtual IReadOnlyList<string> UpdateProperties => [.. WriteableProperties
        .Where(p => !p.Equals(nameof(BaseEntity.Uid), StringComparison.OrdinalIgnoreCase)
                 && !p.Equals(nameof(BaseEntity.Version), StringComparison.OrdinalIgnoreCase))];

    private static readonly Lazy<IReadOnlyList<string>> WriteablePropertyCache = new(GetWriteableProperties);

    private static List<string> GetWriteableProperties()
    {
        // 这里的排除列表你可以根据自己的 BaseEntity 随意调整
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CreateTime",
            "UpdateTime",
            "CreatedTime",
            "UpdatedTime"
        };

        return [.. typeof(T).GetProperties()
            .Where(p => p.CanWrite && !excluded.Contains(p.Name))
            .Select(p => p.Name)];
    }


    public virtual async Task<bool> ExistsIdAsync(Guid uid)
    {
        var sql = $"SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) WHERE [Uid] = @Uid";
        return await Connection.ExecuteScalarAsync<int>(sql, new { Uid = uid }, transaction: Transaction) > 0;
    }

    public virtual async Task<IReadOnlyList<Guid>> GetExistingIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
    {
        if (uidList == null || !uidList.Any()) return [];

        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT [Uid] FROM [{TableName}] {lockHint} WHERE [Uid] IN @Uids";
        var result = await Connection.QueryAsync<Guid>(sql, new { Uids = uidList }, transaction: Transaction);
        return [.. result];
    }

    public virtual async Task<T?> GetByIdAsync(Guid uid, bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [Uid] = @Uid AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QuerySingleOrDefaultAsync<T>(sql, new { Uid = uid }, transaction: Transaction);
    }

    public virtual async Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
    {
        if (uidList == null || !uidList.Any()) return [];

        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [Uid] IN @Uids AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        var result = await Connection.QueryAsync<T>(sql, new { Uids = uidList }, transaction: Transaction);
        return [.. result];
    }

    public virtual async Task<T?> GetSingleEntityAsync()
    {
        var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK)";
        return await Connection.QueryFirstOrDefaultAsync<T>(sql, transaction: Transaction);
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL ORDER BY ISNULL(UpdatedDate, CreatedDate) DESC";
        var result = await Connection.QueryAsync<T>(sql, transaction: Transaction);
        return [.. result];
    }

    public virtual async Task<Guid> AddAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        PrepareForInsert(entity);
        var sql = BuildInsertSql();
        await Connection.ExecuteAsync(sql, entity, transaction: Transaction);
        return entity.Uid;
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var originalVersion = PrepareForUpdate(entity);
        var sql = BuildUpdateSql();
        var parameters = BuildUpdateParameters(entity, originalVersion);
        var result = await Connection.QuerySingleOrDefaultAsync<T>(sql, parameters, transaction: Transaction);
        return result ?? throw CreateConcurrencyException(entity, originalVersion);
    }

    public virtual async Task<int> DeleteSoftAsync(Guid uid)
    {
        var sql = BuildSoftDeleteSql();
        return await Connection.ExecuteAsync(sql, BuildDeleteParameters(uid), transaction: Transaction);
    }

    public virtual async Task<int> DeleteAsync(Guid uid, bool softDelete = true)
    {
        var sql = softDelete ? BuildSoftDeleteSql() : BuildHardDeleteSql();
        return await Connection.ExecuteAsync(sql, BuildDeleteParameters(uid), transaction: Transaction);
    }

    protected virtual string GetLockHint(bool withUpdLock) => withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

    protected virtual void PrepareForInsert(T entity)
    {
        _auditStamp.StampForInsert(entity);
        entity.IsDeleted = false;
        entity.Uid = GuidSequentialGenerator.NewGuid();
        if (entity.Version <= 0) entity.Version = 1;
    }

    protected virtual int PrepareForUpdate(T entity)
    {
        _auditStamp.StampForUpdate(entity);

        var originalVersion = entity.Version <= 0 ? 1 : entity.Version;
        entity.Version = originalVersion + 1;
        return originalVersion;
    }

    protected virtual string BuildInsertSql()
    {
        var columns = string.Join(", ", InsertProperties.Select(p => $"[{p}]"));
        var values = string.Join(", ", InsertProperties.Select(p => $"@{p}"));
        return $"INSERT INTO [{TableName}] ({columns}) VALUES ({values});";
    }

    protected virtual string BuildUpdateSql()
    {
        var setClause = string.Join(", ", UpdateProperties.Select(p => $"[{p}] = @{p}"));
        if (!string.IsNullOrWhiteSpace(setClause))
        {
            setClause += ", ";
        }

        setClause += "[Version] = [Version] + 1";

        return $@"
                UPDATE [{TableName}]
                SET {setClause}
                OUTPUT INSERTED.*
                WHERE [Uid] = @Uid AND [Version] = @OriginalVersion;";
    }

    protected virtual DynamicParameters BuildUpdateParameters(T entity, int originalVersion)
    {
        var parameters = new DynamicParameters(entity);
        parameters.Add("OriginalVersion", originalVersion);
        return parameters;
    }

    protected virtual string BuildSoftDeleteSql()
    {
        return $"UPDATE [{TableName}] SET UpdatedDate = @UpdatedDate, IsDeleted = 1 WHERE [Uid] = @Uid";
    }

    protected virtual string BuildHardDeleteSql()
    {
        return $"DELETE FROM [{TableName}] WHERE [Uid] = @Uid";
    }

    protected virtual object BuildDeleteParameters(Guid uid)
    {
        return new { Uid = uid, UpdatedDate = DateTime.Now };
    }

    protected virtual DBConcurrencyException CreateConcurrencyException(T entity, int originalVersion)
    {
        return new DBConcurrencyException(
            $"[{TableName}] 更新失败：数据已被其他用户修改（Uid={entity.Uid}, Version={originalVersion}）。");
    }

    protected Task<IReadOnlyList<T>> GetListCoreAsync(string whereSql, object? param = null, bool withUpdLock = false)
    {
        return GetListByWhereAsync(whereSql, param, withUpdLock);
    }

    protected Task<IReadOnlyList<T>> GetListCoreAsync(object? criteriaObject, bool withUpdLock = false)
    {
        return GetListByObjectAsync(criteriaObject, withUpdLock);
    }

    protected Task<IReadOnlyList<T>> GetListCoreAsync(QueryCriteria criteria, bool withUpdLock = false)
    {
        return GetListByCriteriaAsync(criteria, withUpdLock);
    }

    protected Task<PagedResult<T>> GetPagedListCoreAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC")
    {
        return GetPagedListInternalAsync(criteria, pageIndex, pageSize, orderBy);
    }

    protected Task<IReadOnlyList<Guid>> AddRangeCoreAsync(IEnumerable<T> entities)
    {
        return AddRangeInternalAsync(entities);
    }

    protected Task<int> UpdateRangeCoreAsync(IEnumerable<T> entities)
    {
        return UpdateRangeInternalAsync(entities);
    }

    protected Task<int> DeleteRangeCoreAsync(IEnumerable<Guid> uidList, bool softDelete = true)
    {
        return DeleteRangeInternalAsync(uidList, softDelete);
    }

    protected Task<int> DeleteRangeSoftCoreAsync(IEnumerable<Guid> uidList)
    {
        return DeleteRangeInternalAsync(uidList, softDelete: true);
    }

    private async Task<IReadOnlyList<T>> GetListByWhereAsync(string whereSql, object? param = null, bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL";

        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            var condition = whereSql.Trim();
            if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                condition = condition[5..];
            }

            sql += $" AND ({condition})";
        }

        var result = await Connection.QueryAsync<T>(sql, param, transaction: Transaction);
        return result.AsList();
    }

    private async Task<IReadOnlyList<T>> GetListByObjectAsync(object? criteriaObject, bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE (IsDeleted = 0 OR IsDeleted IS NULL)";

        if (criteriaObject != null)
        {
            var props = criteriaObject.GetType().GetProperties();
            if (props.Length > 0)
            {
                var conditions = string.Join(" AND ", props.Select(p => $"[{p.Name}] = @{p.Name}"));
                sql += $" AND {conditions}";
            }
        }

        var result = await Connection.QueryAsync<T>(sql, criteriaObject, transaction: Transaction);
        return [.. result];
    }

    private async Task<IReadOnlyList<T>> GetListByCriteriaAsync(QueryCriteria criteria, bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var sqlBuilder = new StringBuilder($"SELECT * FROM [{TableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL");
        var parameters = new DynamicParameters();
        sqlBuilder.Append(ParseQueryCriteria(criteria, parameters));

        var sql = sqlBuilder.ToString();
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("执行动态查询 [{TableName}]，生成的 SQL: {Sql}", TableName, sql);
        }

        var result = await Connection.QueryAsync<T>(sql, parameters, transaction: Transaction);
        return [.. result];
    }

    private async Task<PagedResult<T>> GetPagedListInternalAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy)
    {
        orderBy = NormalizeOrderBy(orderBy, "CreatedDate DESC");

        var sqlBuilder = new StringBuilder("WHERE (IsDeleted = 0 OR IsDeleted IS NULL)");
        var parameters = new DynamicParameters();
        sqlBuilder.Append(ParseQueryCriteria(criteria, parameters));

        if (pageIndex < 1) pageIndex = 1;
        parameters.Add("@Offset", (pageIndex - 1) * pageSize);
        parameters.Add("@Limit", pageSize);

        var sql = $@"
                    SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) {sqlBuilder};
                    SELECT * FROM [{TableName}] WITH(NOLOCK) {sqlBuilder}
                    ORDER BY {orderBy}
                    OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("执行分页动态查询 [{TableName}]，生成的 SQL: {Sql}", TableName, sql);
        }

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<T>();

        return new PagedResult<T>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    private async Task<IReadOnlyList<Guid>> AddRangeInternalAsync(IEnumerable<T> entities)
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.ToList();
        if (entityList.Count == 0) return [];

        foreach (var entity in entityList)
        {
            _auditStamp.StampForInsert(entity);
            entity.IsDeleted = false;
            entity.Uid = GuidSequentialGenerator.NewGuid();
            if (entity.Version <= 0) entity.Version = 1;
        }

        var props = InsertProperties
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var columns = string.Join(", ", InsertProperties.Select(c => $"[{c}]"));
        var valueRows = new List<string>();
        var parameters = new DynamicParameters();

        for (int i = 0; i < entityList.Count; i++)
        {
            var rowParams = new List<string>();
            foreach (var prop in props)
            {
                var paramName = $"p{i}_{prop.Name}";
                parameters.Add(paramName, prop.GetValue(entityList[i]));
                rowParams.Add($"@{paramName}");
            }

            valueRows.Add($"({string.Join(", ", rowParams)})");
        }

        var sql = $@"
                    INSERT INTO [{TableName}] ({columns})
                    OUTPUT INSERTED.[Uid]
                    VALUES {string.Join(", ", valueRows)};";

        var newUids = (await Connection.QueryAsync<Guid>(sql, parameters, transaction: Transaction)).ToList();
        for (int i = 0; i < newUids.Count && i < entityList.Count; i++)
        {
            entityList[i].Uid = newUids[i];
        }

        return newUids;
    }

    private async Task<int> UpdateRangeInternalAsync(IEnumerable<T> entities)
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.OrderBy(x => x.Uid).ToList();
        if (entityList.Count == 0) return 0;

        foreach (var entity in entityList)
        {
            _auditStamp.StampForUpdate(entity);
        }

        var updateProps = UpdateProperties
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var parameters = new DynamicParameters();
        var sqlBuilder = new StringBuilder();

        for (int i = 0; i < entityList.Count; i++)
        {
            var entity = entityList[i];
            var originalVersion = entity.Version <= 0 ? 1 : entity.Version;

            var setClauses = updateProps.Select(prop =>
            {
                var paramName = $"p{i}_{prop.Name}";
                parameters.Add(paramName, prop.GetValue(entity));
                return $"[{prop.Name}] = @{paramName}";
            }).ToList();
            setClauses.Add("[Version] = [Version] + 1");

            var uidParamName = $"p{i}_Uid";
            var verParamName = $"p{i}_OriginalVersion";
            parameters.Add(uidParamName, entity.Uid);
            parameters.Add(verParamName, originalVersion);

            sqlBuilder.AppendLine($@"
                                UPDATE [{TableName}]
                                SET {string.Join(", ", setClauses)}
                                OUTPUT INSERTED.[Uid], INSERTED.[Version]
                                WHERE [Uid] = @{uidParamName} AND [Version] = @{verParamName};");
        }

        var results = new List<UpdateResult>();
        using (var multi = await Connection.QueryMultipleAsync(sqlBuilder.ToString(), parameters, transaction: Transaction))
        {
            while (!multi.IsConsumed)
            {
                var row = await multi.ReadSingleOrDefaultAsync<UpdateResult>();
                if (row != null) results.Add(row);
            }
        }

        if (results.Count < entityList.Count)
        {
            var updatedUids = results.Select(x => x.Uid).ToHashSet();
            var conflictUids = entityList.Select(x => x.Uid).Where(uid => !updatedUids.Contains(uid)).ToList();
            throw new DBConcurrencyException($"并发冲突：以下 Uid 更新失败（可能已被他人修改）=> {string.Join(",", conflictUids)}");
        }

        foreach (var result in results)
        {
            var entity = entityList.First(x => x.Uid == result.Uid);
            entity.Version = result.Version;
        }

        return results.Count;
    }

    private async Task<int> DeleteRangeInternalAsync(IEnumerable<Guid> uidList, bool softDelete = true)
    {
        if (uidList == null || !uidList.Any()) return 0;

        var sql = softDelete
            ? $"UPDATE [{TableName}] SET IsDeleted = 1 WHERE Uid IN @UidList"
            : $"DELETE FROM [{TableName}] WHERE Uid IN @UidList";

        return await Connection.ExecuteAsync(sql, new { UidList = uidList }, transaction: Transaction);
    }

    private static string NormalizeOrderBy(string? orderBy, string defaultOrderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy)) return defaultOrderBy;

        var safeOrderBy = Regex.Replace(orderBy, @"[^a-zA-Z0-9_,\s\.\[\]]", string.Empty);
        return string.IsNullOrWhiteSpace(safeOrderBy) ? defaultOrderBy : safeOrderBy;
    }

    private static string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters, string prefix = "")
    {
        if (criteria == null || (criteria.Items.Count == 0 && criteria.OrGroups.Count == 0))
        {
            return string.Empty;
        }

        var sqlBuilder = new StringBuilder();
        int andIndex = 0;
        foreach (var item in criteria.Items)
        {
            var fieldName = string.IsNullOrEmpty(prefix) || item.Field.Contains('.')
                ? item.Field
                : $"{prefix}{item.Field}";
            var paramName = $"@{fieldName.Replace(".", "_")}_A{andIndex}";
            sqlBuilder.Append($" AND {BuildConditionSql(fieldName, item.Condition, paramName, parameters)}");
            andIndex++;
        }

        int groupIndex = 0;
        foreach (var groupList in criteria.OrGroups)
        {
            if (groupList.Count == 0) continue;

            var orParts = new List<string>();
            int orItemIndex = 0;

            foreach (var item in groupList)
            {
                var fieldName = string.IsNullOrEmpty(prefix) || item.Field.Contains('.')
                    ? item.Field
                    : $"{prefix}{item.Field}";
                var paramName = $"@Or_{groupIndex}_{fieldName.Replace(".", "_")}_{orItemIndex}";
                orParts.Add(BuildConditionSql(fieldName, item.Condition, paramName, parameters));
                orItemIndex++;
            }

            if (orParts.Count > 0)
            {
                sqlBuilder.Append($" AND ({string.Join(" OR ", orParts)})");
            }

            groupIndex++;
        }

        return sqlBuilder.ToString();
    }

    private static string BuildConditionSql(string propName, QueryCondition cond, string paramName, DynamicParameters parameters)
    {
        string safeField;
        if (propName.Contains('.'))
        {
            var parts = propName.Split('.');
            var field = parts[1].Replace("[", string.Empty).Replace("]", string.Empty);
            safeField = $"{parts[0]}.[{field}]";
        }
        else
        {
            safeField = $"[{propName.Replace("[", string.Empty).Replace("]", string.Empty)}]";
        }

        switch (cond.操作符)
        {
            case 查询操作符.等于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} = {paramName}";
            case 查询操作符.模糊匹配:
                parameters.Add(paramName, $"%{cond.值1}%");
                return $"{safeField} LIKE {paramName}";
            case 查询操作符.大于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} > {paramName}";
            case 查询操作符.小于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} < {paramName}";
            case 查询操作符.大于等于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} >= {paramName}";
            case 查询操作符.小于等于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} <= {paramName}";
            case 查询操作符.区间:
                var pStart = $"{paramName}_Start";
                var pEnd = $"{paramName}_End";
                parameters.Add(pStart, cond.值1);
                parameters.Add(pEnd, cond.值2);
                return $"{safeField} BETWEEN {pStart} AND {pEnd}";
            case 查询操作符.包含某值:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} IN {paramName}";
            default:
                return "1=1";
        }
    }

    private sealed class UpdateResult
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
    }
}
