using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Models.Auth;

namespace SwiftExport.Core.Interfaces.Auth.Permissions;

/// <summary>提供有效角色权限投影与账号选择所需的持久化读取端口。</summary>
public interface IUserPermissionRepository
{
    /// <summary>读取活动账号的有效角色和角色权限事实；账号无效时返回空。</summary>
    Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>返回管理员选择目标所需的最小活动用户列表。</summary>
    Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default);

}
