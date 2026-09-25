using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Models.Auth;

namespace SwiftExport.Core.Interfaces.Auth.Permissions;

/// <summary>提供统一权限投影和管理员整组维护所需的持久化端口。</summary>
public interface IUserPermissionRepository
{
    /// <summary>读取活动账号的真实角色与直接模块事实；账号无效时返回空。</summary>
    Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>返回管理员选择目标所需的最小活动用户列表。</summary>
    Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>在调用方串行化事务中重验边界并替换目标普通用户的完整模块集合。</summary>
    Task<UserModuleReplaceStatus> ReplaceModulesAsync(
        Guid actorUserId,
        Guid targetUserId,
        IReadOnlyCollection<string> modulePermissions,
        CancellationToken cancellationToken = default);
}

/// <summary>整组模块替换的持久化结果，区分授权边界和目标状态变化。</summary>
public enum UserModuleReplaceStatus
{
    /// <summary>模块集合已完整替换。</summary>
    Updated = 0,
    /// <summary>操作者不是有效 Admin。</summary>
    ActorNotAdmin = 1,
    /// <summary>目标用户不存在或不可用。</summary>
    TargetNotFound = 2,
    /// <summary>目标用户是权限固定的 Admin。</summary>
    TargetIsAdmin = 3
}
