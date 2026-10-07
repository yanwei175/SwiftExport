using System.Data;
using System.Data.Common;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Tools.AdminPasswordRecovery;

/// <summary>在独立 PostgreSQL 串行化事务内恢复既有活动管理员；不创建账号、提升角色或重试业务。</summary>
public sealed class PostgreSqlAdminPasswordRecoveryStore(string connectionString) : IAdminPasswordRecoveryStore
{
    /// <summary>按原身份、双版本与最终角色资格条件更新五个字段；取消和数据库异常原样传播。</summary>
    public async Task<AdminPasswordRecoveryStatus> ResetExistingAdminAsync(
        string userName, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        // 与源工具一致，开始失败发生在业务 try 之外；连接仍由独立入口释放。
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            // 先参与角色目录排队，再建立首次读取快照；与RBAC、资格和目录引导保持角色→用户顺序。
            // 单纯用户FOR UPDATE不会保护尚未提交的新管理员，不能在该SELECT之后补角色锁。
            await connection.ExecuteAsync(new CommandDefinition(
                $"LOCK TABLE {Table<SysRole>()} IN SHARE ROW EXCLUSIVE MODE;",
                transaction: transaction, cancellationToken: cancellationToken));
            var users = Table<SysUser>();
            var uid = Column<SysUser>(nameof(SysUser.Uid));
            var version = Column<SysUser>(nameof(SysUser.Version));
            var credential = Column<SysUser>(nameof(SysUser.CredentialVersion));
            var active = Column<SysUser>(nameof(SysUser.IsActive));
            var deleted = Column<SysUser>(nameof(SysUser.IsDeleted));
            var name = Column<SysUser>(nameof(SysUser.UserName));
            var selectSql = $"""
                SELECT u.{uid},u.{version},u.{credential},EXISTS({AdminQuery("u." + uid)})
                FROM {users} AS u
                WHERE rtrim(u.{name}, ' ') COLLATE public.erp_dictionary_ci
                    = rtrim(@UserName, ' ') COLLATE public.erp_dictionary_ci
                  AND u.{active}=TRUE AND COALESCE(u.{deleted},FALSE)=FALSE
                LIMIT 1 FOR UPDATE OF u;
                """;
            Guid userId;
            int expectedVersion;
            int expectedCredential;
            bool isAdmin;
            // 使用真实类型读取器保留非空版本/布尔的读取失败，不把 NULL 映射为默认值。
            await using (var reader = (DbDataReader)await connection.ExecuteReaderAsync(new CommandDefinition(
                selectSql, new RecoveryParameters(userName), transaction, cancellationToken: cancellationToken)))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await reader.DisposeAsync();
                    await transaction.RollbackAsync(cancellationToken);
                    return AdminPasswordRecoveryStatus.TargetNotFound;
                }
                userId = reader.GetGuid(0);
                expectedVersion = reader.GetInt32(1);
                expectedCredential = reader.GetInt32(2);
                isAdmin = reader.GetBoolean(3);
            }
            if (!isAdmin)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AdminPasswordRecoveryStatus.TargetNotAdmin;
            }

            var updateSql = $"""
                UPDATE {users} AS u
                SET {Column<SysUser>(nameof(SysUser.PasswordHash))}=@PasswordHash,
                    {credential}=u.{credential}+1,
                    {version}=u.{version}+1,
                    {Column<SysUser>(nameof(SysUser.UpdatedDate))}=clock_timestamp()::timestamp,
                    {Column<SysUser>(nameof(SysUser.UpdatedBy))}='LocalAdminPasswordRecovery'
                WHERE u.{uid}=@UserId AND u.{version}=@Version AND u.{credential}=@CredentialVersion
                  AND u.{active}=TRUE AND COALESCE(u.{deleted},FALSE)=FALSE
                  AND EXISTS({AdminQuery("u." + uid)});
                """;
            var parameters = new RecoveryParameters(userName, passwordHash, userId, expectedVersion, expectedCredential);
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                updateSql, parameters, transaction, cancellationToken: cancellationToken));
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AdminPasswordRecoveryStatus.ConcurrencyConflict;
            }
            await transaction.CommitAsync(cancellationToken);
            return AdminPasswordRecoveryStatus.Updated;
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* 清理失败不遮蔽原异常；不将未知提交或序列化终止冒称成功。 */ }
            throw;
        }
    }

    /// <summary>生成固定内部身份表达式的角色资格；只接受有效关联与活动角色，不要求系统角色标记。</summary>
    private static string AdminQuery(string userId) => $"""
        SELECT 1 FROM {Table<SysUserRole>()} AS ur
        JOIN {Table<SysRole>()} AS r
          ON r.{Column<SysRole>(nameof(SysRole.Uid))}=ur.{Column<SysUserRole>(nameof(SysUserRole.RoleId))}
         AND COALESCE(r.{Column<SysRole>(nameof(SysRole.IsDeleted))},FALSE)=FALSE
         AND r.{Column<SysRole>(nameof(SysRole.IsActive))}=TRUE
        WHERE ur.{Column<SysUserRole>(nameof(SysUserRole.UserId))}={userId}
          AND COALESCE(ur.{Column<SysUserRole>(nameof(SysUserRole.IsDeleted))},FALSE)=FALSE
          AND rtrim(r.{Column<SysRole>(nameof(SysRole.RoleCode))}, ' ') COLLATE public.erp_dictionary_ci
              = rtrim('SYSTEM_ADMIN', ' ') COLLATE public.erp_dictionary_ci
        """;

    /// <summary>复用实体映射取得引用后的物理表名，避免旧 SQL 表名的复数约定污染 PG 名称。</summary>
    private static string Table<T>()
    {
        var metadata = EntityMetadataCache.GetMetadata(typeof(T));
        return $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
    }

    /// <summary>通过同一元数据取得持久化列，不另建字段映射。</summary>
    private static string Column<T>(string property) => PostgreSqlNames.Quote(
        EntityMetadataCache.GetMetadata(typeof(T)).Columns.Single(x => x.PropertyName == property).ColumnName);

    /// <summary>为本工具两条命令显式绑定 text 和身份/版本；可空文本保留数据库 NULL，不做业务清洗。</summary>
    private sealed class RecoveryParameters(string? userName, string? passwordHash = null,
        Guid? userId = null, int? version = null, int? credential = null) : SqlMapper.IDynamicParameters
    {
        /// <summary>给当前原生命令添加类型明确的参数，不共享驱动参数实例或接管事务。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            command.Parameters.Add(new NpgsqlParameter("UserName", NpgsqlDbType.Text) { Value = (object?)userName ?? DBNull.Value });
            if (!userId.HasValue) return;
            command.Parameters.Add(new NpgsqlParameter("PasswordHash", NpgsqlDbType.Text) { Value = (object?)passwordHash ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("UserId", NpgsqlDbType.Uuid) { Value = userId.Value });
            command.Parameters.Add(new NpgsqlParameter("Version", NpgsqlDbType.Integer) { Value = version!.Value });
            command.Parameters.Add(new NpgsqlParameter("CredentialVersion", NpgsqlDbType.Integer) { Value = credential!.Value });
        }
    }
}

