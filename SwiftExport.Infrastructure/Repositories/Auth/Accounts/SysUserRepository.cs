using Dapper;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Infrastructure.Data;
using System;

namespace SwiftExport.Infrastructure.Repositories.Auth.Accounts;

/// <summary>
/// SysUser 数据访问实现（仅认证场景使用）。
/// </summary>
public class SysUserRepository(DbSession session) : ISysUserRepository
{
    private readonly DbSession _session = session;

    /// <summary>根据用户名查询用户（用于登录验证）</summary>
    public async Task<SysUser?> GetByUserNameAsync(string userName)
    {
        const string sql = """
            SELECT Uid, UserName, NickName, PasswordHash, Email, PhoneNumber,
                   IsActive, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, IsDeleted
            FROM [SysUser] WITH(NOLOCK)
            WHERE UserName = @UserName AND IsActive = 1 AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        return await _session.Connection.QuerySingleOrDefaultAsync<SysUser>(sql, new { UserName = userName });
    }

    /// <summary>根据 Uid 查询用户</summary>
    public async Task<SysUser?> GetByIdAsync(Guid uid)
    {
        const string sql = """
            SELECT Uid, UserName, NickName, PasswordHash, Email, PhoneNumber,
                   IsActive, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, IsDeleted
            FROM [SysUser] WITH(NOLOCK)
            WHERE Uid = @Uid AND IsActive = 1 AND (IsDeleted = 0 OR IsDeleted IS NULL)
            """;
        return await _session.Connection.QuerySingleOrDefaultAsync<SysUser>(sql, new { Uid = uid });
    }
}
