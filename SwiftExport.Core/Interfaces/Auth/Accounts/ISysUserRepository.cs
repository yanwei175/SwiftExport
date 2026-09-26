using SwiftExport.Core.Entities.Auth;
using System;

namespace SwiftExport.Core.Interfaces.Auth.Accounts;

/// <summary>
/// 用户仓储接口：提供认证相关查询。
/// </summary>
public interface ISysUserRepository
{
    /// <summary>根据用户名查询用户（含 PasswordHash），用于登录验证。</summary>
    Task<SysUser?> GetByUserNameAsync(string userName);

    /// <summary>根据 Uid 查询用户。</summary>
    Task<SysUser?> GetByIdAsync(Guid uid);
}
