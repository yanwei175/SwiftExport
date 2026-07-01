using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Common;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using System.Data;
using System.Reflection;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>
/// 复杂仓储操作的内部复用工具。
/// 对外能力必须由具体业务仓储显式声明，避免重新污染轻量 IRepository。
/// </summary>
internal static partial class DapperRepositoryComplexHelper
{
    public static async Task<IReadOnlyList<T>> GetListByWhereAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        string whereSql,
        object? param = null,
        bool withUpdLock = false) where T : BaseEntity
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT * FROM [{tableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL";

        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            var condition = whereSql.Trim();
            if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                condition = condition[5..];
            }

            sql += $" AND ({condition})";
        }

        var result = await connection.QueryAsync<T>(sql, param, transaction: transaction);
        return result.AsList();
    }

    public static async Task<IReadOnlyList<T>> GetListByObjectAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        object? criteriaObject,
        bool withUpdLock = false) where T : BaseEntity
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT * FROM [{tableName}] {lockHint} WHERE (IsDeleted = 0 OR IsDeleted IS NULL)";

        if (criteriaObject != null)
        {
            var props = criteriaObject.GetType().GetProperties();
            if (props.Length > 0)
            {
                var conditions = string.Join(" AND ", props.Select(p => $"[{p.Name}] = @{p.Name}"));
                sql += $" AND {conditions}";
            }
        }

        var result = await connection.QueryAsync<T>(sql, criteriaObject, transaction: transaction);
        return [.. result];
    }

    public static async Task<IReadOnlyList<T>> GetListByCriteriaAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        QueryCriteria criteria,
        ILogger? logger = null,
        bool withUpdLock = false) where T : BaseEntity
    {
        var lockHint = GetLockHint(withUpdLock);
        var sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL");
        var parameters = new DynamicParameters();
        sqlBuilder.Append(ParseQueryCriteria(criteria, parameters));

        var sql = sqlBuilder.ToString();
        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("执行动态查询 [{TableName}]，生成的 SQL: {Sql}", tableName, sql);
        }

        var result = await connection.QueryAsync<T>(sql, parameters, transaction: transaction);
        return [.. result];
    }

    public static async Task<PagedResult<T>> GetPagedListAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        QueryCriteria criteria,
        int pageIndex,
        int pageSize,
        string orderBy,
        ILogger? logger = null) where T : BaseEntity
    {
        orderBy = NormalizeOrderBy(orderBy, "CreatedDate DESC");

        var sqlBuilder = new StringBuilder("WHERE (IsDeleted = 0 OR IsDeleted IS NULL)");
        var parameters = new DynamicParameters();
        sqlBuilder.Append(ParseQueryCriteria(criteria, parameters));

        if (pageIndex < 1) pageIndex = 1;
        parameters.Add("@Offset", (pageIndex - 1) * pageSize);
        parameters.Add("@Limit", pageSize);

        var sql = $@"
SELECT COUNT(1) FROM [{tableName}] WITH(NOLOCK) {sqlBuilder};
SELECT * FROM [{tableName}] WITH(NOLOCK) {sqlBuilder}
ORDER BY {orderBy}
OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("执行分页动态查询 [{TableName}]，生成的 SQL: {Sql}", tableName, sql);
        }

        using var multi = await connection.QueryMultipleAsync(sql, parameters, transaction: transaction);
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

    public static async Task<IReadOnlyList<Guid>> AddRangeAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        IAuditStampService auditStamp,
        IEnumerable<T> entities) where T : BaseEntity
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.ToList();
        if (entityList.Count == 0) return [];

        foreach (var entity in entityList)
        {
            auditStamp.StampForInsert(entity);
            entity.IsDeleted = false;
            entity.Uid = GuidSequentialGenerator.NewGuid();
            if (entity.Version <= 0) entity.Version = 1;
        }

        var writeableProps = GetWriteableProperties<T>();
        var props = writeableProps
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var columns = string.Join(", ", writeableProps.Select(c => $"[{c}]"));
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
INSERT INTO [{tableName}] ({columns})
OUTPUT INSERTED.[Uid]
VALUES {string.Join(", ", valueRows)};";

        var newUids = (await connection.QueryAsync<Guid>(sql, parameters, transaction: transaction)).ToList();
        for (int i = 0; i < newUids.Count && i < entityList.Count; i++)
        {
            entityList[i].Uid = newUids[i];
        }

        return newUids;
    }

    public static async Task<int> UpdateRangeAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        IAuditStampService auditStamp,
        IEnumerable<T> entities) where T : BaseEntity
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.OrderBy(x => x.Uid).ToList();
        if (entityList.Count == 0) return 0;

        foreach (var entity in entityList)
        {
            auditStamp.StampForUpdate(entity);
        }

        var updateProps = GetWriteableProperties<T>()
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null && p.Name != nameof(BaseEntity.Version) && p.Name != nameof(BaseEntity.Uid))
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
UPDATE [{tableName}]
SET {string.Join(", ", setClauses)}
OUTPUT INSERTED.[Uid], INSERTED.[Version]
WHERE [Uid] = @{uidParamName} AND [Version] = @{verParamName};");
        }

        var results = new List<UpdateResult>();
        using (var multi = await connection.QueryMultipleAsync(sqlBuilder.ToString(), parameters, transaction: transaction))
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

    public static async Task<int> DeleteRangeAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        IEnumerable<Guid> uidList,
        bool softDelete = true) where T : BaseEntity
    {
        if (uidList == null || !uidList.Any()) return 0;

        var sql = softDelete
            ? $"UPDATE [{tableName}] SET IsDeleted = 1 WHERE Uid IN @UidList"
            : $"DELETE FROM [{tableName}] WHERE Uid IN @UidList";

        return await connection.ExecuteAsync(sql, new { UidList = uidList }, transaction: transaction);
    }

    public static Task<int> DeleteRangeSoftAsync<T>(
        IDbConnection connection,
        IDbTransaction? transaction,
        string tableName,
        IEnumerable<Guid> uidList) where T : BaseEntity
    {
        return DeleteRangeAsync<T>(connection, transaction, tableName, uidList, softDelete: true);
    }

    public static string NormalizeOrderBy(string? orderBy, string defaultOrderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy)) return defaultOrderBy;

        var safeOrderBy = OrderByRegex().Replace(orderBy, string.Empty);
        return string.IsNullOrWhiteSpace(safeOrderBy) ? defaultOrderBy : safeOrderBy;
    }

    public static string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters, string prefix = "")
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

    private static IReadOnlyList<string> GetWriteableProperties<T>() where T : BaseEntity
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IsDeleted",
            "CreateTime",
            "UpdateTime",
            "CreatedTime",
            "UpdatedTime"
        };

        return typeof(T).GetProperties()
            .Where(p => p.CanWrite && !excluded.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
    }

    private static string GetLockHint(bool withUpdLock) => withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
    private static partial System.Text.RegularExpressions.Regex OrderByRegex();

    private sealed class UpdateResult
    {
        public Guid Uid { get; set; }
        public int Version { get; set; }
    }
}
