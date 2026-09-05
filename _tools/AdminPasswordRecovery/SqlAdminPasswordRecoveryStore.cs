using Microsoft.Data.SqlClient;

namespace SwiftExport.Tools.AdminPasswordRecovery;

/// <summary>通过参数化 SQL 在串行化事务内恢复既有活动 Admin。</summary>
public sealed class SqlAdminPasswordRecoveryStore(string connectionString) : IAdminPasswordRecoveryStore
{
    /// <inheritdoc />
    public async Task<AdminPasswordRecoveryStatus> ResetExistingAdminAsync(
        string userName,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            const string selectSql = """
                SELECT TOP(1) u.Uid,u.Version,u.CredentialVersion,
                    CAST(CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.SysUser_Roles ur
                        JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
                        WHERE ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0 AND r.RoleName=N'Admin'
                    ) THEN 1 ELSE 0 END AS bit) IsAdmin
                FROM dbo.SysUser u WITH(UPDLOCK,HOLDLOCK)
                WHERE u.UserName=@UserName AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0;
                """;
            await using var select = new SqlCommand(selectSql, connection, transaction);
            select.Parameters.AddWithValue("@UserName", userName);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancellationToken);
                return AdminPasswordRecoveryStatus.TargetNotFound;
            }

            var userId = reader.GetGuid(0);
            var version = reader.GetInt32(1);
            var credentialVersion = reader.GetInt32(2);
            var isAdmin = reader.GetBoolean(3);
            await reader.DisposeAsync();
            if (!isAdmin)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AdminPasswordRecoveryStatus.TargetNotAdmin;
            }

            const string updateSql = """
                UPDATE u
                SET PasswordHash=@PasswordHash,
                    CredentialVersion=u.CredentialVersion+1,
                    Version=u.Version+1,
                    UpdatedDate=GETDATE(),
                    UpdatedBy=N'LocalAdminPasswordRecovery'
                FROM dbo.SysUser u
                WHERE u.Uid=@UserId AND u.Version=@Version AND u.CredentialVersion=@CredentialVersion
                  AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
                  AND EXISTS(
                      SELECT 1 FROM dbo.SysUser_Roles ur
                      JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
                      WHERE ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0 AND r.RoleName=N'Admin');
                """;
            await using var update = new SqlCommand(updateSql, connection, transaction);
            update.Parameters.AddWithValue("@PasswordHash", passwordHash);
            update.Parameters.AddWithValue("@UserId", userId);
            update.Parameters.AddWithValue("@Version", version);
            update.Parameters.AddWithValue("@CredentialVersion", credentialVersion);
            var rows = await update.ExecuteNonQueryAsync(cancellationToken);
            if (rows != 1)
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
            catch { /* 原始异常优先；连接释放会清理失败事务。 */ }
            throw;
        }
    }
}
