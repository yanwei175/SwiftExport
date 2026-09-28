using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.AuthApis
{
    /// <summary>前后端共享的用户、角色及权限目录管理 HTTP 契约。</summary>
    public interface ISystemRbacApi
    {
        /// <summary>分页查询用户，包括停用账号。</summary>
        [Post("/api/system/users/page")]
        Task<ServiceResult<PagedResponseBase<ManagedUserDto>>> SearchUsersAsync([Body] ManagedPageRequest request);

        /// <summary>读取用户详情。</summary>
        [Get("/api/system/users/{id}")]
        Task<ServiceResult<ManagedUserDto>> GetUserAsync(Guid id);

        /// <summary>更新用户非凭据资料。</summary>
        [Put("/api/system/users/{id}")]
        Task<ServiceResult<ManagedUserDto>> UpdateUserAsync(Guid id, [Body] UpdateManagedUserRequest request);

        /// <summary>启停用户并保护最后管理员。</summary>
        [Put("/api/system/users/{id}/status")]
        Task<ServiceResult<ManagedUserDto>> SetUserStatusAsync(Guid id, [Body] SetManagedStatusRequest request);

        /// <summary>读取用户当前有效角色。</summary>
        [Get("/api/system/users/{id}/roles")]
        Task<ServiceResult<UserRolesDto>> GetUserRolesAsync(Guid id);

        /// <summary>按用户版本整组替换角色关系。</summary>
        [Put("/api/system/users/{id}/roles")]
        Task<ServiceResult<UserRolesDto>> ReplaceUserRolesAsync(Guid id, [Body] ReplaceUserRolesRequest request);

        /// <summary>查看用户当前有效权限。</summary>
        [Get("/api/system/users/{id}/effective-permissions")]
        Task<ServiceResult<List<string>>> GetEffectivePermissionsAsync(Guid id);

        /// <summary>分页查询角色，包括停用角色。</summary>
        [Post("/api/system/roles/page")]
        Task<ServiceResult<PagedResponseBase<ManagedRoleDto>>> SearchRolesAsync([Body] ManagedPageRequest request);

        /// <summary>读取角色详情。</summary>
        [Get("/api/system/roles/{id}")]
        Task<ServiceResult<ManagedRoleDto>> GetRoleAsync(Guid id);

        /// <summary>创建普通角色。</summary>
        [Post("/api/system/roles")]
        Task<ServiceResult<ManagedRoleDto>> CreateRoleAsync([Body] CreateManagedRoleRequest request);

        /// <summary>更新角色显示资料。</summary>
        [Put("/api/system/roles/{id}")]
        Task<ServiceResult<ManagedRoleDto>> UpdateRoleAsync(Guid id, [Body] UpdateManagedRoleRequest request);

        /// <summary>启停普通角色。</summary>
        [Put("/api/system/roles/{id}/status")]
        Task<ServiceResult<ManagedRoleDto>> SetRoleStatusAsync(Guid id, [Body] SetManagedStatusRequest request);

        /// <summary>删除无用户关联的普通角色。</summary>
        [Delete("/api/system/roles/{id}")]
        Task<ServiceResult<bool>> DeleteRoleAsync(Guid id, [Query] int expectedVersion);

        /// <summary>读取角色权限或内置管理员自动授权说明。</summary>
        [Get("/api/system/roles/{id}/permissions")]
        Task<ServiceResult<RolePermissionsDto>> GetRolePermissionsAsync(Guid id);

        /// <summary>按角色版本整组替换权限。</summary>
        [Put("/api/system/roles/{id}/permissions")]
        Task<ServiceResult<RolePermissionsDto>> ReplaceRolePermissionsAsync(Guid id, [Body] ReplaceRolePermissionsRequest request);

        /// <summary>读取不可在线修改的权限目录。</summary>
        [Get("/api/system/permissions")]
        Task<ServiceResult<List<ManagedPermissionDto>>> GetPermissionsAsync();
    }
}
