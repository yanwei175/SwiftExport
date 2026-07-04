using Dapper;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories.Auth;

/// <summary>
/// 用户-角色关联表数据访问实现，对应 [dbo].[SysUser_Roles]。
/// 所有查询均使用 NOLOCK，写操作通过 DbSession 的当前连接执行。
/// </summary>
public class SysUserRoleRepository(DbSession session) : ISysUserRoleRepository
{
    private readonly DbSession _session = session;

    // 公共列列表（INSERT 时用）
    private const string SelectColumns = """
        Uid, UserId, UserName, RoleId, RoleName,
        IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy
        """;

    /// <summary>获取指定用户的所有角色（未软删除）</summary>
    public async Task<IEnumerable<SysUserRole>> GetByUserIdAsync(Guid userId)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM [SysUser_Roles] WITH(NOLOCK)
            WHERE UserId = @UserId AND (IsDeleted = 0 OR IsDeleted IS NULL)
            ORDER BY RoleName
            """;
        return await _session.Connection.QueryAsync<SysUserRole>(sql, new { UserId = userId });
    }

    /// <summary>获取拥有指定角色的所有用户（未软删除）</summary>
    public async Task<IEnumerable<SysUserRole>> GetByRoleIdAsync(int roleId)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM [SysUser_Roles] WITH(NOLOCK)
            WHERE RoleId = @RoleId AND (IsDeleted = 0 OR IsDeleted IS NULL)
            ORDER BY UserName
            """;
        return await _session.Connection.QueryAsync<SysUserRole>(sql, new { RoleId = roleId });
    }

    /// <summary>判断用户是否拥有指定角色名（忽略大小写）</summary>
    public async Task<bool> UserHasRoleAsync(Guid userId, string roleName)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM [SysUser_Roles] WITH(NOLOCK)
            WHERE UserId = @UserId
              AND RoleName = @RoleName
              AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        var count = await _session.Connection.ExecuteScalarAsync<int>(
            sql, new { UserId = userId, RoleName = roleName });
        return count > 0;
    }

    /// <summary>插入一条用户-角色关联（CreatedDate/CreatedBy 由 AuditStampService 自动填充）</summary>
    public async Task<Guid> AddAsync(SysUserRole userRole)
    {
        const string sql = """
            INSERT INTO [SysUser_Roles]
                (UserId, UserName, RoleId, RoleName, ChangeTime,
                 IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy)
            OUTPUT INSERTED.Uid
            VALUES
                (@UserId, @UserName, @RoleId, @RoleName, @ChangeTime,
                 0, @CreatedDate, @CreatedBy, @UpdatedDate, @UpdatedBy)
            """;
        return await _session.Connection.ExecuteScalarAsync<Guid>(sql, userRole);
    }

    /// <summary>软删除单条关联记录（撤销角色）</summary>
    public async Task<bool> RemoveAsync(Guid uid)
    {
        const string sql = """
            UPDATE [SysUser_Roles]
            SET IsDeleted = 1, UpdatedDate = GETDATE()
            WHERE Uid = @Uid
            """;
        var rows = await _session.Connection.ExecuteAsync(sql, new { Uid = uid });
        return rows > 0;
    }

    /// <summary>软删除用户的所有角色关联（重新分配前先清空）</summary>
    public async Task<Guid> RemoveAllByUserIdAsync(Guid userId)
    {
        const string sql = """
            UPDATE [SysUser_Roles]
            SET IsDeleted = 1, UpdatedDate = GETDATE()
            WHERE UserId = @UserId AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        return await _session.Connection.ExecuteScalarAsync<Guid>(sql, new { UserId = userId });
    }
}
