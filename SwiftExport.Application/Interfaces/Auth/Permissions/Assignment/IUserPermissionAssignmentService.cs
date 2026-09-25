using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Auth.Permissions.Assignment;

/// <summary>定义真实 Admin 读取与整组替换用户直接权限的入口。</summary>
public interface IUserPermissionAssignmentService
{
    /// <summary>读取目标用户直接可分配的权限集合。</summary>
    Task<ServiceResult<UserModulePermissionsDto>> GetUserModulesAsync(Guid userId, CancellationToken cancellationToken = default);
    /// <summary>在一个事务中替换普通用户的完整权限集合。</summary>
    Task<ServiceResult<UserModulePermissionsDto>> SaveUserModulesAsync(Guid userId, SaveUserModulePermissionsRequestDto request, CancellationToken cancellationToken = default);
}
