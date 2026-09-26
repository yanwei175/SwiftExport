using SwiftExport.Application.Models.Auth.Permissions.Evaluation;

namespace SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;

/// <summary>定义运行时唯一权限投影与稳定权限键判断入口。</summary>
public interface IPermissionEvaluationService
{
    /// <summary>读取活动账号的统一权限投影，无效账号返回空。</summary>
    Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default);
    /// <summary>按统一投影判断权限，未知键一律不放行。</summary>
    Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default);
}
