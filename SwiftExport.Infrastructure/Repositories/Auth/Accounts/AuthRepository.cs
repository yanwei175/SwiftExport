using Dapper;
using Microsoft.Data.SqlClient;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Auth.Accounts;

/// <summary>实现认证和密码维护所需的参数化 SQL，并参与当前请求的 DbSession。</summary>
public sealed class AuthRepository(RepositoryBaseContext context) : IAuthRepository
{
    /// <inheritdoc />
    public Task<SysUser?> GetActiveUserByNameAsync(string userName, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<SysUser>(Command("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                   UserName,NickName,PasswordHash,CredentialVersion,Email,PhoneNumber,IsActive
            FROM dbo.SysUser
            WHERE UserName=@UserName AND IsActive=1 AND ISNULL(IsDeleted,0)=0;
            """, new { UserName = userName }, cancellationToken));

    /// <inheritdoc />
    public Task<SysUser?> GetActiveUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<SysUser>(Command("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                   UserName,NickName,PasswordHash,CredentialVersion,Email,PhoneNumber,IsActive
            FROM dbo.SysUser
            WHERE Uid=@UserId AND IsActive=1 AND ISNULL(IsDeleted,0)=0;
            """, new { UserId = userId }, cancellationToken));

    /// <inheritdoc />
    public Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1
                FROM dbo.SysUser u
                JOIN dbo.SysUser_Roles ur ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
                JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
                WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
                  AND r.RoleCode=N'SYSTEM_ADMIN'
            ) THEN 1 ELSE 0 END AS bit);
            """, new { UserId = userId }, cancellationToken));

    /// <inheritdoc />
    public Task<bool> IsUserNameInUseAsync(string userName, CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1 FROM dbo.SysUser WHERE UserName=@UserName AND ISNULL(IsDeleted,0)=0
            ) THEN 1 ELSE 0 END AS bit);
            """, new { UserName = userName }, cancellationToken));

    /// <inheritdoc />
    public async Task<AuthUserCreateStatus> CreateUserByAdminAsync(
        Guid actorUserId,
        SysUser user,
        CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForInsert(user);
        try
        {
            var status = await context.Session.Connection.ExecuteScalarAsync<int>(Command("""
                SET NOCOUNT ON;
                IF EXISTS(SELECT 1 FROM dbo.SysUser WITH(UPDLOCK,HOLDLOCK)
                          WHERE UserName=@UserName AND ISNULL(IsDeleted,0)=0)
                BEGIN SELECT 2; RETURN; END;

                INSERT dbo.SysUser
                    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                     UserName,NickName,PasswordHash,CredentialVersion,Email,PhoneNumber,IsActive)
                SELECT @Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
                       @UserName,@NickName,@PasswordHash,@CredentialVersion,@Email,@PhoneNumber,1
                WHERE EXISTS(
                    SELECT 1
                    FROM dbo.SysUser actor
                    JOIN dbo.SysUser_Roles ur ON ur.UserId=actor.Uid AND ISNULL(ur.IsDeleted,0)=0
                    JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
                    WHERE actor.Uid=@ActorUserId AND actor.IsActive=1 AND ISNULL(actor.IsDeleted,0)=0
                      AND r.RoleCode=N'SYSTEM_ADMIN');
                SELECT CASE WHEN @@ROWCOUNT=1 THEN 0 ELSE 1 END;
                """, new
                {
                    ActorUserId = actorUserId,
                    user.Uid,
                    user.Version,
                    user.CreatedDate,
                    user.CreatedBy,
                    user.UpdatedDate,
                    user.UpdatedBy,
                    user.UserName,
                    user.NickName,
                    user.PasswordHash,
                    user.CredentialVersion,
                    user.Email,
                    user.PhoneNumber
                }, cancellationToken));
            return (AuthUserCreateStatus)status;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            // 活动用户名唯一索引是并发创建的最终门禁，冲突仍归一化为可处理的业务结果。
            return AuthUserCreateStatus.UserNameInUse;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateOwnPasswordAsync(SysUser user, CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForUpdate(user);
        var rows = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysUser
            SET PasswordHash=@PasswordHash,
                CredentialVersion=CredentialVersion+1,
                Version=Version+1,
                UpdatedDate=@UpdatedDate,
                UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND CredentialVersion=@CredentialVersion
              AND IsActive=1 AND ISNULL(IsDeleted,0)=0;
            """, user, cancellationToken));
        return rows == 1;
    }

    /// <inheritdoc />
    public async Task<AdminPasswordResetStatus> ResetNonAdminPasswordAsync(
        Guid actorUserId,
        SysUser targetUser,
        CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForUpdate(targetUser);
        var status = await context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            SET NOCOUNT ON;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.SysUser actor
                JOIN dbo.SysUser_Roles aur ON aur.UserId=actor.Uid AND ISNULL(aur.IsDeleted,0)=0
                JOIN dbo.SysRoles ar ON ar.Uid=aur.RoleId AND ISNULL(ar.IsDeleted,0)=0 AND ar.IsActive=1
                WHERE actor.Uid=@ActorUserId AND actor.IsActive=1 AND ISNULL(actor.IsDeleted,0)=0
                  AND ar.RoleCode=N'SYSTEM_ADMIN')
            BEGIN SELECT 1; RETURN; END;

            IF NOT EXISTS(SELECT 1 FROM dbo.SysUser
                          WHERE Uid=@Uid AND IsActive=1 AND ISNULL(IsDeleted,0)=0)
            BEGIN SELECT 2; RETURN; END;

            IF EXISTS(SELECT 1 FROM dbo.SysUser_Roles tur
                      JOIN dbo.SysRoles tr ON tr.Uid=tur.RoleId AND ISNULL(tr.IsDeleted,0)=0 AND tr.IsActive=1
                      WHERE tur.UserId=@Uid AND ISNULL(tur.IsDeleted,0)=0 AND tr.RoleCode=N'SYSTEM_ADMIN')
            BEGIN SELECT 3; RETURN; END;

            UPDATE target
            SET PasswordHash=@PasswordHash,
                CredentialVersion=target.CredentialVersion+1,
                Version=target.Version+1,
                UpdatedDate=@UpdatedDate,
                UpdatedBy=@UpdatedBy
            FROM dbo.SysUser target
            WHERE target.Uid=@Uid AND target.Version=@Version
              AND target.CredentialVersion=@CredentialVersion
              AND target.IsActive=1 AND ISNULL(target.IsDeleted,0)=0
              AND EXISTS(
                  SELECT 1 FROM dbo.SysUser actor
                  JOIN dbo.SysUser_Roles aur ON aur.UserId=actor.Uid AND ISNULL(aur.IsDeleted,0)=0
                  JOIN dbo.SysRoles ar ON ar.Uid=aur.RoleId AND ISNULL(ar.IsDeleted,0)=0 AND ar.IsActive=1
                  WHERE actor.Uid=@ActorUserId AND actor.IsActive=1 AND ISNULL(actor.IsDeleted,0)=0
                    AND ar.RoleCode=N'SYSTEM_ADMIN')
              AND NOT EXISTS(
                  SELECT 1 FROM dbo.SysUser_Roles tur
                  JOIN dbo.SysRoles tr ON tr.Uid=tur.RoleId AND ISNULL(tr.IsDeleted,0)=0 AND tr.IsActive=1
                  WHERE tur.UserId=target.Uid AND ISNULL(tur.IsDeleted,0)=0 AND tr.RoleCode=N'SYSTEM_ADMIN');
            SELECT CASE WHEN @@ROWCOUNT=1 THEN 0 ELSE 4 END;
            """, new
            {
                ActorUserId = actorUserId,
                targetUser.Uid,
                targetUser.Version,
                targetUser.CredentialVersion,
                targetUser.PasswordHash,
                targetUser.UpdatedDate,
                targetUser.UpdatedBy
            }, cancellationToken));
        return (AdminPasswordResetStatus)status;
    }

    /// <inheritdoc />
    public Task<bool> IsCredentialVersionValidAsync(
        Guid userId,
        int credentialVersion,
        CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CAST(CASE WHEN EXISTS(
                SELECT 1 FROM dbo.SysUser
                WHERE Uid=@UserId AND CredentialVersion=@CredentialVersion
                  AND IsActive=1 AND ISNULL(IsDeleted,0)=0
            ) THEN 1 ELSE 0 END AS bit);
            """, new { UserId = userId, CredentialVersion = credentialVersion }, cancellationToken));

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);
}
