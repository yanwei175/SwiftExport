using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using System.Data;


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

    protected readonly ICurrentUser CurrentUser = context.CurrentUser;

    protected static readonly EntityMetadata Metadata = EntityMetadataCache.GetMetadata<T>();

    /// <summary>未限定表名供既有专用仓储使用；通用 CRUD 使用显式 schema 和转义标识符。</summary>
    protected static readonly string TableName = Metadata.TableName;
    private static readonly string QualifiedTableName = $"{Quote(Metadata.SchemaName)}.{Quote(Metadata.TableName)}";
    private static readonly string SelectColumns = string.Join(", ", Metadata.Columns.Select(column =>
        $"{Quote(column.ColumnName)} AS {Quote(column.PropertyName)}"));

    /// <summary>仅在 SQL Server 实现中引用标识符，参数始终使用 CLR 属性名。</summary>
    private static string Quote(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
    private static string Column(string propertyName) => Quote(Metadata.GetProperty(propertyName).ColumnName);


    protected virtual IReadOnlyList<string> WriteableProperties => WriteablePropertyCache.Value;
    protected virtual IReadOnlyList<string> InsertProperties => WriteableProperties;
    protected virtual IReadOnlyList<string> UpdateProperties => [.. WriteableProperties
        .Where(p => !p.Equals(nameof(BaseEntity.Uid), StringComparison.OrdinalIgnoreCase)
                 && !p.Equals(nameof(BaseEntity.Version), StringComparison.OrdinalIgnoreCase))];

    private static readonly Lazy<IReadOnlyList<string>> WriteablePropertyCache = new(GetWriteableProperties);

    private static List<string> GetWriteableProperties()
    {
        // 保留既有写入排除规则；列映射和忽略字段由共享元数据负责。
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CreateTime",
            "UpdateTime",
            "CreatedTime",
            "UpdatedTime"
        };

        return [.. Metadata.Columns
            .Where(column => !excluded.Contains(column.PropertyName))
            .Select(column => column.PropertyName)];
    }
    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<bool> ExistsIdAsync(Guid uid)
    {
        var sql = $"SELECT COUNT(1) FROM {QualifiedTableName} WITH(NOLOCK) WHERE {Column(nameof(BaseEntity.Uid))} = @Uid";
        return await Connection.ExecuteScalarAsync<int>(sql, new { Uid = uid }, transaction: Transaction) > 0;
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<IReadOnlyList<Guid>> GetExistingIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
    {
        if (uidList == null || !uidList.Any()) return [];

        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT {Column(nameof(BaseEntity.Uid))} FROM {QualifiedTableName} {lockHint} WHERE {Column(nameof(BaseEntity.Uid))} IN @Uids";
        var result = await Connection.QueryAsync<Guid>(sql, new { Uids = uidList }, transaction: Transaction);
        return [.. result];
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<T?> GetByIdAsync(Guid uid, bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT {SelectColumns} FROM {QualifiedTableName} {lockHint} WHERE {Column(nameof(BaseEntity.Uid))} = @Uid AND ({Column(nameof(BaseEntity.IsDeleted))} = 0 OR {Column(nameof(BaseEntity.IsDeleted))} IS NULL)";
        return await Connection.QuerySingleOrDefaultAsync<T>(sql, new { Uid = uid }, transaction: Transaction);
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
    {
        if (uidList == null || !uidList.Any()) return [];

        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT {SelectColumns} FROM {QualifiedTableName} {lockHint} WHERE {Column(nameof(BaseEntity.Uid))} IN @Uids AND ({Column(nameof(BaseEntity.IsDeleted))} = 0 OR {Column(nameof(BaseEntity.IsDeleted))} IS NULL)";
        var result = await Connection.QueryAsync<T>(sql, new { Uids = uidList }, transaction: Transaction);
        return [.. result];
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<T?> GetSingleEntityAsync()
    {
        var sql = $"SELECT TOP 1 {SelectColumns} FROM {QualifiedTableName} WITH(NOLOCK)";
        return await Connection.QueryFirstOrDefaultAsync<T>(sql, transaction: Transaction);
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var sql = $"SELECT {SelectColumns} FROM {QualifiedTableName} WITH(NOLOCK) WHERE {Column(nameof(BaseEntity.IsDeleted))} = 0 OR {Column(nameof(BaseEntity.IsDeleted))} IS NULL ORDER BY ISNULL({Column(nameof(BaseEntity.UpdatedDate))}, {Column(nameof(BaseEntity.CreatedDate))}) DESC";
        var result = await Connection.QueryAsync<T>(sql, transaction: Transaction);
        return [.. result];
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<RepositoryWriteResult> AddAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 自动填充审计字段（CreatedBy/CreatedDate/UpdatedBy/UpdatedDate）
        _auditStamp.StampForInsert(entity);
        entity.IsDeleted = false; // 确保新增时 IsDeleted 字段为 false
        entity.Uid = GuidSequentialGenerator.NewGuid(); // 预先生成 Uid，避免后续查询时无法获取到主键值
        if (entity.Version <= 0) entity.Version = 1;
        // 使用静态缓存的属性列表，不用每次都反射
        var columns = string.Join(", ", InsertProperties.Select(Column));
        var paramsName = string.Join(", ", InsertProperties.Select(p => $"@{Metadata.GetProperty(p).ParameterName}"));

        var sql = $@"INSERT INTO {QualifiedTableName} ({columns}) VALUES ({paramsName});";

        var rows = await Connection.ExecuteAsync(sql, entity, transaction: Transaction);
        if (rows != 1)
        {
            return RepositoryWriteResult.Fail(
                entity.Uid,
                null,
                "INSERT_FAILED",
                "新增失败。");
        }
        return RepositoryWriteResult.Ok(
            entity.Uid,
            null,
            entity.Version,
            "新增成功。");
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<RepositoryWriteResult> UpdateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // 自动填充审计字段（UpdatedBy/UpdatedDate 强制刷新）
        _auditStamp.StampForUpdate(entity);


        var originalVersion = entity.Version <= 0 ? 1 : entity.Version;
        entity.Version = originalVersion + 1;

        // ★ Version 从 writeableProps 排除，由数据库自增
        var setClause = string.Join(", ",
            UpdateProperties
                .Where(p => p != "Version" && p != "Uid")
                .Select(p => $"{Column(p)} = @{Metadata.GetProperty(p).ParameterName}"))
            + $", {Column(nameof(BaseEntity.Version))} = {Column(nameof(BaseEntity.Version))} + 1";


        var sql = $@"
                    UPDATE {QualifiedTableName}
                    SET {setClause}
                    OUTPUT INSERTED.{Column(nameof(BaseEntity.Version))}
                    WHERE {Column(nameof(BaseEntity.Uid))} = @Uid AND {Column(nameof(BaseEntity.Version))} = @OriginalVersion
                    AND {Column(nameof(BaseEntity.IsDeleted))}=0";

        var parameters = new DynamicParameters(entity);
        parameters.Add("OriginalVersion", originalVersion);

        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(sql, parameters, transaction: Transaction);

        if (newVersion is null)
        {
            return RepositoryWriteResult.Fail(
                entity.Uid,
                entity.Version,
                "CONCURRENCY_CONFLICT",
                "资料已被其他人修改或删除，请刷新后重试。");
        }

        return RepositoryWriteResult.Ok(
            entity.Uid,
            entity.Version,
            newVersion.Value,
            "更新成功。");
    }

    /// <summary>在当前会话及事务中执行单实体操作，使用共享物理列映射并保留原有并发和软删除语义。</summary>
    public virtual async Task<RepositoryWriteResult> DeleteAsync(Guid uid, int version, bool softDelete = true)
    {
        if (softDelete)
        {
            var sql = $@"UPDATE {QualifiedTableName}
                        SET {Column(nameof(BaseEntity.UpdatedDate))} = @UpdatedDate,
                            {Column(nameof(BaseEntity.UpdatedBy))} = @UpdatedBy,
                            {Column(nameof(BaseEntity.IsDeleted))} = 1
                        OUTPUT INSERTED.{Column(nameof(BaseEntity.Version))}
                        WHERE {Column(nameof(BaseEntity.Uid))} = @Uid
                             AND {Column(nameof(BaseEntity.Version))} = @Version
                             AND {Column(nameof(BaseEntity.IsDeleted))} = 0
                             ";
            var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(sql, new
            {
                Uid = uid,
                Version = version,
                UpdatedDate = DateTime.Now,
                UpdatedBy = CurrentUser.UserName
            }, transaction: Transaction);

            if (newVersion is null)
            {
                return RepositoryWriteResult.Fail(
                    uid,
                    version,
                    "CONCURRENCY_CONFLICT",
                    "删除失败，可能已被其他人修改或删除。");
            }

            return RepositoryWriteResult.Ok(
                uid,
                version,
                newVersion.Value,
                "删除成功。");
        }

        var sql2 = $"DELETE FROM {QualifiedTableName} WHERE {Column(nameof(BaseEntity.Uid))} = @Uid AND {Column(nameof(BaseEntity.Version))} = @Version";
        var rows2 = await Connection.ExecuteAsync(sql2, new
        {
            Uid = uid,
            Version = version
        }, transaction: Transaction);

        if (rows2 != 1)
        {
            return RepositoryWriteResult.Fail(
                uid,
                version,
                "CONCURRENCY_CONFLICT",
                "删除失败，客户资料可能已被其他人修改或删除。");
        }

        return RepositoryWriteResult.Ok(
            uid,
            version,
            null,
            "删除成功。");
    }

    protected virtual string GetLockHint(bool withUpdLock) => withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";


}
