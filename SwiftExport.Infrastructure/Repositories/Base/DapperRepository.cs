using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Common;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;

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

    protected virtual string TableName => typeof(T).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(T).Name;
    protected virtual IReadOnlyList<string> WriteableProperties => WriteablePropertyCache.Value;
    protected virtual IReadOnlyList<string> InsertProperties => WriteableProperties;
    protected virtual IReadOnlyList<string> UpdateProperties => WriteableProperties
        .Where(p => !p.Equals(nameof(BaseEntity.Uid), StringComparison.OrdinalIgnoreCase)
                 && !p.Equals(nameof(BaseEntity.Version), StringComparison.OrdinalIgnoreCase))
        .ToList();

    private static readonly Lazy<IReadOnlyList<string>> WriteablePropertyCache = new(GetWriteableProperties);

    private static List<string> GetWriteableProperties()
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
        return DapperRepositoryComplexHelper.GetListByWhereAsync<T>(
            Connection, Transaction, TableName, whereSql, param, withUpdLock);
    }

    protected Task<IReadOnlyList<T>> GetListCoreAsync(object? criteriaObject, bool withUpdLock = false)
    {
        return DapperRepositoryComplexHelper.GetListByObjectAsync<T>(
            Connection, Transaction, TableName, criteriaObject, withUpdLock);
    }

    protected Task<IReadOnlyList<T>> GetListCoreAsync(QueryCriteria criteria, bool withUpdLock = false)
    {
        return DapperRepositoryComplexHelper.GetListByCriteriaAsync<T>(
            Connection, Transaction, TableName, criteria, Logger, withUpdLock);
    }

    protected Task<PagedResult<T>> GetPagedListCoreAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "CreatedDate DESC")
    {
        return DapperRepositoryComplexHelper.GetPagedListAsync<T>(
            Connection, Transaction, TableName, criteria, pageIndex, pageSize, orderBy, Logger);
    }

    protected Task<IReadOnlyList<Guid>> AddRangeCoreAsync(IEnumerable<T> entities)
    {
        return DapperRepositoryComplexHelper.AddRangeAsync(
            Connection, Transaction, TableName, AuditStamp, entities);
    }

    protected Task<int> UpdateRangeCoreAsync(IEnumerable<T> entities)
    {
        return DapperRepositoryComplexHelper.UpdateRangeAsync(
            Connection, Transaction, TableName, AuditStamp, entities);
    }

    protected Task<int> DeleteRangeCoreAsync(IEnumerable<Guid> uidList, bool softDelete = true)
    {
        return DapperRepositoryComplexHelper.DeleteRangeAsync<T>(
            Connection, Transaction, TableName, uidList, softDelete);
    }

    protected Task<int> DeleteRangeSoftCoreAsync(IEnumerable<Guid> uidList)
    {
        return DapperRepositoryComplexHelper.DeleteRangeSoftAsync<T>(
            Connection, Transaction, TableName, uidList);
    }
}
