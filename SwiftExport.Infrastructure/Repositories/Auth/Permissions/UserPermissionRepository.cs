using Dapper;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Models.Auth;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Auth.Permissions;

/// <summary>读取真实角色和直接模块关系，并在调用方事务内执行整组替换。</summary>
public sealed class UserPermissionRepository(RepositoryBaseContext context) : IUserPermissionRepository
{
    /// <inheritdoc />
    public async Task<UserPermissionFacts?> GetPermissionFactsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        using var grid = await context.Session.Connection.QueryMultipleAsync(Command("""
            SELECT u.Uid AS UserId,CAST(1 AS bit) AS IsActive,
                   CAST(CASE WHEN EXISTS(
                       SELECT 1 FROM dbo.SysUser_Roles ur WITH(HOLDLOCK)
                       JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
                       WHERE ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0 AND r.RoleName=N'Admin'
                   ) THEN 1 ELSE 0 END AS bit) AS IsAdmin
            FROM dbo.SysUser u WITH(HOLDLOCK)
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0;

            SELECT DISTINCT r.RoleName
            FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
            JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
              AND NULLIF(r.RoleName,N'') IS NOT NULL
            ORDER BY r.RoleName;

            SELECT DISTINCT p.PermissionKey
            FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_ModulePermissions p WITH(HOLDLOCK)
              ON p.UserId=u.Uid AND ISNULL(p.IsDeleted,0)=0
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
              AND NULLIF(p.PermissionKey,N'') IS NOT NULL
            ORDER BY p.PermissionKey;
            """, new { UserId = userId }, cancellationToken));
        var identity = await grid.ReadSingleOrDefaultAsync<PermissionIdentity>();
        var roles = (await grid.ReadAsync<string>()).AsList();
        var modules = (await grid.ReadAsync<string>()).AsList();
        return identity is null ? null : new UserPermissionFacts
        {
            UserId = identity.UserId,
            IsActive = identity.IsActive,
            IsAdmin = identity.IsAdmin,
            RoleNames = roles,
            ModulePermissions = modules
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await context.Session.Connection.QueryAsync<UserSummaryDto>(Command("""
            SELECT u.Uid,u.UserName,
                   CASE WHEN NULLIF(u.NickName,N'') IS NULL THEN u.UserName ELSE u.NickName END AS NickName,
                   CAST(CASE WHEN EXISTS(
                       SELECT 1 FROM dbo.SysUser_Roles ur
                       JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0
                       WHERE ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0 AND r.RoleName=N'Admin'
                   ) THEN 1 ELSE 0 END AS bit) AS IsAdmin
            FROM dbo.SysUser u
            WHERE u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
            ORDER BY u.UserName,u.Uid;
            """, null, cancellationToken));
        return users.AsList();
    }

    /// <inheritdoc />
    public async Task<UserModuleReplaceStatus> ReplaceModulesAsync(
        Guid actorUserId,
        Guid targetUserId,
        IReadOnlyCollection<string> modulePermissions,
        CancellationToken cancellationToken = default)
    {
        _ = context.Session.Transaction ?? throw new InvalidOperationException("模块整组替换需要调用方事务。");
        var status = (UserModuleReplaceStatus)await context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            SET NOCOUNT ON;
            IF NOT EXISTS(
                SELECT 1 FROM dbo.SysUser actor WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.SysUser_Roles aur WITH(UPDLOCK,HOLDLOCK)
                  ON aur.UserId=actor.Uid AND ISNULL(aur.IsDeleted,0)=0
                JOIN dbo.SysRoles ar WITH(HOLDLOCK)
                  ON ar.Uid=aur.RoleId AND ISNULL(ar.IsDeleted,0)=0
                WHERE actor.Uid=@ActorUserId AND actor.IsActive=1 AND ISNULL(actor.IsDeleted,0)=0
                  AND ar.RoleName=N'Admin')
            BEGIN SELECT 1; RETURN; END;

            IF NOT EXISTS(SELECT 1 FROM dbo.SysUser WITH(UPDLOCK,HOLDLOCK)
                          WHERE Uid=@TargetUserId AND IsActive=1 AND ISNULL(IsDeleted,0)=0)
            BEGIN SELECT 2; RETURN; END;

            IF EXISTS(
                SELECT 1 FROM dbo.SysUser_Roles tur WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.SysRoles tr WITH(HOLDLOCK)
                  ON tr.Uid=tur.RoleId AND ISNULL(tr.IsDeleted,0)=0
                WHERE tur.UserId=@TargetUserId AND ISNULL(tur.IsDeleted,0)=0 AND tr.RoleName=N'Admin')
            BEGIN SELECT 3; RETURN; END;

            SELECT 0;
            """, new { ActorUserId = actorUserId, TargetUserId = targetUserId }, cancellationToken));
        if (status != UserModuleReplaceStatus.Updated)
            return status;

        var audit = new SysUserModulePermission();
        context.AuditStamp.StampForInsert(audit);
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.SysUser_ModulePermissions
            SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE UserId=@TargetUserId AND ISNULL(IsDeleted,0)=0
              AND (@HasModules=0 OR PermissionKey NOT IN @ModulePermissions);
            """, new
        {
            TargetUserId = targetUserId,
            HasModules = modulePermissions.Count > 0,
            ModulePermissions = modulePermissions,
            audit.UpdatedDate,
            audit.UpdatedBy
        }, cancellationToken));

        foreach (var permission in modulePermissions)
        {
            var relation = new SysUserModulePermission { UserId = targetUserId, PermissionKey = permission };
            context.AuditStamp.StampForInsert(relation);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.SysUser_ModulePermissions
                    (Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,UserId,PermissionKey)
                SELECT @Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@UserId,@PermissionKey
                WHERE NOT EXISTS(
                    SELECT 1 FROM dbo.SysUser_ModulePermissions WITH(UPDLOCK,HOLDLOCK)
                    WHERE UserId=@UserId AND PermissionKey=@PermissionKey AND ISNULL(IsDeleted,0)=0);
                """, relation, cancellationToken));
        }

        return UserModuleReplaceStatus.Updated;
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken);

    private sealed class PermissionIdentity
    {
        public Guid UserId { get; set; }
        public bool IsActive { get; set; }
        public bool IsAdmin { get; set; }
    }
}
