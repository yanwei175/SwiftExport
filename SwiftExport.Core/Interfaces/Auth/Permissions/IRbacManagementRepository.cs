using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Auth.Permissions;

/// <summary>提供扁平角色授权管理的查询、聚合锁定及版本化写入，所有写入参与调用方事务。</summary>
public interface IRbacManagementRepository
{
    /// <summary>分页读取用户，包括停用账号。</summary>
    Task<PagedResponseBase<ManagedUserDto>> SearchUsersAsync(ManagedPageRequest request, CancellationToken cancellationToken);
    /// <summary>读取用户资料，不返回凭据。</summary>
    Task<ManagedUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>读取用户聚合并持有更新锁。</summary>
    Task<ManagedUserDto?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>读取用户当前有效角色标识。</summary>
    Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid userId, CancellationToken cancellationToken);
    /// <summary>分页读取角色，包括停用角色。</summary>
    Task<PagedResponseBase<ManagedRoleDto>> SearchRolesAsync(ManagedPageRequest request, CancellationToken cancellationToken);
    /// <summary>读取角色资料。</summary>
    Task<ManagedRoleDto?> GetRoleAsync(Guid roleId, CancellationToken cancellationToken);
    /// <summary>读取角色聚合并持有更新锁。</summary>
    Task<ManagedRoleDto?> GetRoleForUpdateAsync(Guid roleId, CancellationToken cancellationToken);
    /// <summary>读取指定角色集合中的有效角色，用于整组保存校验。</summary>
    Task<IReadOnlyList<ManagedRoleDto>> GetActiveRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
    /// <summary>读取角色当前有效权限定义标识。</summary>
    Task<IReadOnlyList<Guid>> GetRolePermissionIdsAsync(Guid roleId, CancellationToken cancellationToken);
    /// <summary>读取数据库中的活动权限目录投影。</summary>
    Task<IReadOnlyList<ManagedPermissionDto>> GetPermissionsAsync(CancellationToken cancellationToken);
    /// <summary>读取指定权限定义集合中的活动项，用于整组保存校验。</summary>
    Task<IReadOnlyList<ManagedPermissionDto>> GetActivePermissionsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken);
    /// <summary>先锁定内置角色并在同一事务内确认操作者仍为有效管理员。</summary>
    Task<bool> LockAndCheckAdministratorAsync(Guid actorUserId, CancellationToken cancellationToken);
    /// <summary>统计当前活动管理员人数；须在内置角色锁定后调用。</summary>
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);
    /// <summary>按预期版本更新用户资料或状态并打审计戳。</summary>
    Task<bool> UpdateUserAsync(Guid userId, int expectedVersion, string? nickName, string? email, string? phoneNumber, bool? isActive, CancellationToken cancellationToken);
    /// <summary>整组替换用户角色并递增用户版本。</summary>
    Task ReplaceUserRolesAsync(Guid userId, int expectedVersion, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
    /// <summary>新增普通角色并返回标识。</summary>
    Task<Guid> CreateRoleAsync(string roleCode, string roleName, string? description, CancellationToken cancellationToken);
    /// <summary>按预期版本更新角色资料、状态或软删除。</summary>
    Task<bool> UpdateRoleAsync(Guid roleId, int expectedVersion, string? roleName, string? description, bool? isActive, bool delete, CancellationToken cancellationToken);
    /// <summary>统计当前有效用户角色关系，删除角色前必须为零。</summary>
    Task<int> CountRoleUsersAsync(Guid roleId, CancellationToken cancellationToken);
    /// <summary>整组替换普通角色权限并递增角色版本。</summary>
    Task ReplaceRolePermissionsAsync(Guid roleId, int expectedVersion, IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken);
}
