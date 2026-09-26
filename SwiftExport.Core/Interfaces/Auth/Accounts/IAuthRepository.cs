using SwiftExport.Core.Entities.Auth;

namespace SwiftExport.Core.Interfaces.Auth.Accounts;

/// <summary>提供认证、账号创建和密码维护所需的原子持久化能力。</summary>
public interface IAuthRepository
{
    /// <summary>按规范化用户名读取活动账号及密码凭据。</summary>
    Task<SysUser?> GetActiveUserByNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>按标识读取活动账号及密码凭据。</summary>
    Task<SysUser?> GetActiveUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>判断活动账号是否拥有有效 Admin 角色。</summary>
    Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>判断未删除用户名是否已占用，包括停用账号。</summary>
    Task<bool> IsUserNameInUseAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>仅在操作者仍为有效 Admin 时创建用户；返回是否写入。</summary>
    Task<AuthUserCreateStatus> CreateUserByAdminAsync(Guid actorUserId, SysUser user, CancellationToken cancellationToken = default);

    /// <summary>按行版本与凭据版本原子修改本人密码并递增两个版本。</summary>
    Task<bool> UpdateOwnPasswordAsync(SysUser user, CancellationToken cancellationToken = default);

    /// <summary>仅在操作者为 Admin 且目标不是 Admin 时原子重置密码并递增版本。</summary>
    Task<AdminPasswordResetStatus> ResetNonAdminPasswordAsync(
        Guid actorUserId,
        SysUser targetUser,
        CancellationToken cancellationToken = default);

    /// <summary>验证账号仍有效且当前凭据版本与 Token 一致。</summary>
    Task<bool> IsCredentialVersionValidAsync(Guid userId, int credentialVersion, CancellationToken cancellationToken = default);
}

/// <summary>管理员重置密码的持久化结果，用于区分安全边界与并发变化。</summary>
public enum AdminPasswordResetStatus
{
    /// <summary>密码已更新。</summary>
    Updated = 0,
    /// <summary>操作者不再是有效 Admin。</summary>
    ActorNotAdmin = 1,
    /// <summary>目标账号不存在或不可用。</summary>
    TargetNotFound = 2,
    /// <summary>目标账号是 Admin。</summary>
    TargetIsAdmin = 3,
    /// <summary>目标账号版本已变化。</summary>
    ConcurrencyConflict = 4
}

/// <summary>创建用户的原子持久化结果。</summary>
public enum AuthUserCreateStatus
{
    /// <summary>用户已创建。</summary>
    Created = 0,
    /// <summary>操作者不再是有效 Admin。</summary>
    ActorNotAdmin = 1,
    /// <summary>规范化用户名已被未删除账号占用。</summary>
    UserNameInUse = 2
}
