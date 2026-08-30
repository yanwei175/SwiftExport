using Dapper;
using Microsoft.Extensions.Logging;
using {{ProjectName}}.Contracts.Common;
using {{ProjectName}}.Core.Common;
using {{ProjectName}}.Core.Interfaces;
using {{ProjectName}}.Infrastructure.Contexts;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;

namespace {{ProjectName}}.Infrastructure.Repositories.Base;

public partial class DapperRepository<TEntity>(RepositoryBaseContext context) : IRepository<TEntity>
    where TEntity : BaseEntity
{
    protected static readonly string TableName = typeof(TEntity).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(TEntity).Name;
    private static readonly List<string> InsertProps = GetInsertProperties();
    private static readonly List<string> UpdateProps = GetUpdateProperties();

    protected IDbConnection Connection => context.Session.Connection;
    protected IDbTransaction? Transaction => context.Session.Transaction;
    protected ILogger Logger => context.LoggerFactory.CreateLogger(GetType());

    private static List<string> GetInsertProperties()
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(BaseEntity.IsDeleted)
        };

        return typeof(TEntity).GetProperties()
            .Where(p => p.CanWrite && !excluded.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
    }

    private static List<string> GetUpdateProperties()
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(BaseEntity.Uid),
            nameof(BaseEntity.Version),
            nameof(BaseEntity.IsDeleted),
            nameof(BaseEntity.CreatedDate),
            nameof(BaseEntity.CreatedBy)
        };

        return typeof(TEntity).GetProperties()
            .Where(p => p.CanWrite && !excluded.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
    }

    public virtual async Task<bool> ExistsIdAsync(Guid uid)
    {
        var sql = $"SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) WHERE [Uid] = @Uid";
        return await Connection.ExecuteScalarAsync<int>(sql, new { Uid = uid }, Transaction) > 0;
    }

    public virtual async Task<TEntity?> GetByIdAsync(Guid uid, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH(NOLOCK)";
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [Uid] = @Uid AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QuerySingleOrDefaultAsync<TEntity>(sql, new { Uid = uid }, Transaction);
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetByIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
    {
        var list = uidList?.ToList() ?? [];
        if (list.Count == 0) return [];

        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH(NOLOCK)";
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [Uid] IN @Uids AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return [.. await Connection.QueryAsync<TEntity>(sql, new { Uids = list }, Transaction)];
    }

    public virtual async Task<TEntity?> GetSingleEntityAsync()
    {
        var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL";
        return await Connection.QueryFirstOrDefaultAsync<TEntity>(sql, transaction: Transaction);
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync()
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL ORDER BY ISNULL(UpdatedDate, CreatedDate) DESC";
        return [.. await Connection.QueryAsync<TEntity>(sql, transaction: Transaction)];
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetListAsync(string whereSql, object? param = null, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH(NOLOCK)";
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL";

        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            var condition = whereSql.Trim();
            if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
                condition = condition[5..];

            sql += $" AND ({condition})";
        }

        return [.. await Connection.QueryAsync<TEntity>(sql, param, Transaction)];
    }

    public virtual async Task<PagedResult<TEntity>> GetPagedListAsync(string? whereSql = null, object? param = null, int pageIndex = 1, int pageSize = 20, string orderBy = "Uid DESC")
    {
        pageIndex = pageIndex < 1 ? 1 : pageIndex;
        pageSize = pageSize < 1 ? 20 : pageSize;
        orderBy = CleanOrderBy(orderBy);

        var where = "WHERE (IsDeleted = 0 OR IsDeleted IS NULL)";
        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            var condition = whereSql.Trim();
            if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
                condition = condition[5..];

            where += $" AND ({condition})";
        }

        var parameters = new DynamicParameters(param);
        parameters.Add("@Offset", (pageIndex - 1) * pageSize);
        parameters.Add("@Limit", pageSize);

        var sql = $@"
SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) {where};
SELECT * FROM [{TableName}] WITH(NOLOCK) {where}
ORDER BY {orderBy}
OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, Transaction);
        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<TEntity>();

        return new PagedResult<TEntity>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public virtual async Task<Guid> AddAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        context.AuditStamp.StampForInsert(entity);
        entity.IsDeleted = false;
        entity.Uid = GuidSequentialGenerator.NewGuid();
        if (entity.Version <= 0) entity.Version = 1;

        var columns = string.Join(", ", InsertProps.Select(x => $"[{x}]"));
        var parameters = string.Join(", ", InsertProps.Select(x => $"@{x}"));
        var sql = $"INSERT INTO [{TableName}] ({columns}) VALUES ({parameters});";
        await Connection.ExecuteAsync(sql, entity, Transaction);
        return entity.Uid;
    }

    public virtual async Task<TEntity> UpdateAsync(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        context.AuditStamp.StampForUpdate(entity);

        var originalVersion = entity.Version <= 0 ? 1 : entity.Version;
        var setClause = string.Join(", ", UpdateProps.Select(x => $"[{x}] = @{x}")) + ", [Version] = [Version] + 1";

        var sql = $@"
UPDATE [{TableName}]
SET {setClause}
OUTPUT INSERTED.*
WHERE [Uid] = @Uid AND [Version] = @OriginalVersion";

        var parameters = new DynamicParameters(entity);
        parameters.Add("OriginalVersion", originalVersion);
        var result = await Connection.QuerySingleOrDefaultAsync<TEntity>(sql, parameters, Transaction);
        return result ?? throw new DBConcurrencyException($"[{TableName}] 更新失败，数据可能已被其他用户修改。");
    }

    public virtual Task<int> DeleteSoftAsync(Guid uid)
        => Connection.ExecuteAsync($"UPDATE [{TableName}] SET UpdatedDate = @UpdatedDate, IsDeleted = 1 WHERE [Uid] = @Uid", new { Uid = uid, UpdatedDate = DateTime.Now }, Transaction);

    public virtual Task<int> DeleteAsync(Guid uid, bool softDelete = true)
        => softDelete
            ? DeleteSoftAsync(uid)
            : Connection.ExecuteAsync($"DELETE FROM [{TableName}] WHERE [Uid] = @Uid", new { Uid = uid }, Transaction);

    protected static string CleanOrderBy(string orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy)) return "Uid DESC";
        return OrderByRegex().Replace(orderBy, "");
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
    protected static partial System.Text.RegularExpressions.Regex OrderByRegex();
}
