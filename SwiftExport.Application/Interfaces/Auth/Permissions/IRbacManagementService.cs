using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Auth.Permissions;

/// <summary>提供管理员用户、角色和权限定义 API 的业务边界。</summary>
public interface IRbacManagementService
{
    /// <summary>分页查询用户，包括停用账号。</summary>
    Task<ServiceResult<PagedResponseBase<ManagedUserDto>>> SearchUsersAsync(ManagedPageRequest request, CancellationToken cancellationToken);
    /// <summary>读取单个用户资料。</summary>
    Task<ServiceResult<ManagedUserDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>更新用户非凭据资料。</summary>
    Task<ServiceResult<ManagedUserDto>> UpdateUserAsync(Guid userId, UpdateManagedUserRequest request, CancellationToken cancellationToken);
    /// <summary>启停用户并防止最后管理员失效。</summary>
    Task<ServiceResult<ManagedUserDto>> SetUserStatusAsync(Guid userId, SetManagedStatusRequest request, CancellationToken cancellationToken);
    /// <summary>读取用户完整有效角色集合。</summary>
    Task<ServiceResult<UserRolesDto>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>按用户版本整组替换角色关系。</summary>
    Task<ServiceResult<UserRolesDto>> ReplaceUserRolesAsync(Guid userId, ReplaceUserRolesRequest request, CancellationToken cancellationToken);
    /// <summary>读取目标用户当前有效权限。</summary>
    Task<ServiceResult<List<string>>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>分页查询角色，包括停用角色。</summary>
    Task<ServiceResult<PagedResponseBase<ManagedRoleDto>>> SearchRolesAsync(ManagedPageRequest request, CancellationToken cancellationToken);
    /// <summary>读取角色详情。</summary>
    Task<ServiceResult<ManagedRoleDto>> GetRoleAsync(Guid roleId, CancellationToken cancellationToken);
    /// <summary>创建普通角色。</summary>
    Task<ServiceResult<ManagedRoleDto>> CreateRoleAsync(CreateManagedRoleRequest request, CancellationToken cancellationToken);
    /// <summary>更新普通角色显示资料。</summary>
    Task<ServiceResult<ManagedRoleDto>> UpdateRoleAsync(Guid roleId, UpdateManagedRoleRequest request, CancellationToken cancellationToken);
    /// <summary>启停普通角色。</summary>
    Task<ServiceResult<ManagedRoleDto>> SetRoleStatusAsync(Guid roleId, SetManagedStatusRequest request, CancellationToken cancellationToken);
    /// <summary>删除没有用户关系的普通角色。</summary>
    Task<ServiceResult<bool>> DeleteRoleAsync(Guid roleId, int expectedVersion, CancellationToken cancellationToken);
    /// <summary>读取角色权限关系或内置管理员自动权限说明。</summary>
    Task<ServiceResult<RolePermissionsDto>> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken);
    /// <summary>按角色版本整组替换可分配权限。</summary>
    Task<ServiceResult<RolePermissionsDto>> ReplaceRolePermissionsAsync(Guid roleId, ReplaceRolePermissionsRequest request, CancellationToken cancellationToken);
    /// <summary>只读获取代码目录对应的数据库权限定义。</summary>
    Task<ServiceResult<List<ManagedPermissionDto>>> GetPermissionsAsync(CancellationToken cancellationToken);
}
