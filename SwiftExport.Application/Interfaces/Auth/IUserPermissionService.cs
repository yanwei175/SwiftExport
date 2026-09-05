using SwiftExport.Application.Models.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Auth;

/// <summary>集中生成权限投影并提供管理员直接模块维护用例。</summary>
public interface IUserPermissionService
{
    /// <summary>读取指定活动账号的统一权限投影；无效账号返回空。</summary>
    Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>按统一投影判断稳定权限键；未知键一律不放行。</summary>
    Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default);

    /// <summary>仅允许当前真实 Admin 获取最小用户列表。</summary>
    Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>仅允许当前真实 Admin 读取目标用户的直接模块集合。</summary>
    Task<ServiceResult<UserModulePermissionsDto>> GetUserModulesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>仅允许当前真实 Admin 原子替换普通用户的完整模块集合。</summary>
    Task<ServiceResult<UserModulePermissionsDto>> SaveUserModulesAsync(
        Guid userId,
        SaveUserModulePermissionsRequestDto request,
        CancellationToken cancellationToken = default);
}
