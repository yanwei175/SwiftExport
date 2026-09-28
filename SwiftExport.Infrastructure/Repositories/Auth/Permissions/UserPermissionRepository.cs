using Dapper;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Models.Auth;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Auth.Permissions;

/// <summary>读取有效角色及权限事实，供认证与统一权限评估使用。</summary>
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
                       JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
                       WHERE ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0 AND r.RoleCode=N'SYSTEM_ADMIN'
                   ) THEN 1 ELSE 0 END AS bit) AS IsAdmin
            FROM dbo.SysUser u WITH(HOLDLOCK)
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0;

            SELECT DISTINCT r.RoleName
            FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
            JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
              AND NULLIF(r.RoleName,N'') IS NOT NULL
            ORDER BY r.RoleName;

            SELECT DISTINCT r.RoleCode
            FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
            JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
            ORDER BY r.RoleCode;

            SELECT DISTINCT p.PermissionKey
            FROM dbo.SysUser u WITH(HOLDLOCK)
            JOIN dbo.SysUser_Roles ur WITH(HOLDLOCK) ON ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0
            JOIN dbo.SysRoles r WITH(HOLDLOCK) ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
            JOIN dbo.SysRole_Permissions rp WITH(HOLDLOCK) ON rp.RoleId=r.Uid AND ISNULL(rp.IsDeleted,0)=0
            JOIN dbo.SysPermissionDefinitions p WITH(HOLDLOCK) ON p.Uid=rp.PermissionId AND ISNULL(p.IsDeleted,0)=0 AND p.IsActive=1
            WHERE u.Uid=@UserId AND u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
            ORDER BY p.PermissionKey;

            """, new { UserId = userId }, cancellationToken));
        var identity = await grid.ReadSingleOrDefaultAsync<PermissionIdentity>();
        var roles = (await grid.ReadAsync<string>()).AsList();
        var roleCodes = (await grid.ReadAsync<string>()).AsList();
        var permissions = (await grid.ReadAsync<string>()).AsList();
        return identity is null ? null : new UserPermissionFacts
        {
            UserId = identity.UserId,
            IsActive = identity.IsActive,
            IsAdmin = identity.IsAdmin,
            RoleNames = roles,
            RoleCodes = roleCodes,
            PermissionKeys = permissions
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
                       JOIN dbo.SysRoles r ON r.Uid=ur.RoleId AND ISNULL(r.IsDeleted,0)=0 AND r.IsActive=1
                       WHERE ur.UserId=u.Uid AND ISNULL(ur.IsDeleted,0)=0 AND r.RoleCode=N'SYSTEM_ADMIN'
                   ) THEN 1 ELSE 0 END AS bit) AS IsAdmin
            FROM dbo.SysUser u
            WHERE u.IsActive=1 AND ISNULL(u.IsDeleted,0)=0
            ORDER BY u.UserName,u.Uid;
            """, null, cancellationToken));
        return users.AsList();
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
