using System;
using System.Collections.Generic;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.Auth
{
    /// <summary>管理员查询用户时返回的资料与版本，不包含密码或凭据哈希。</summary>
    public sealed class ManagedUserDto
    {
        /// <summary>用户标识。</summary>
        public Guid Uid { get; set; }
        /// <summary>并发版本。</summary>
        public int Version { get; set; }
        /// <summary>登录名。</summary>
        public string UserName { get; set; }
        /// <summary>显示名称。</summary>
        public string NickName { get; set; }
        /// <summary>邮箱。</summary>
        public string Email { get; set; }
        /// <summary>联系电话。</summary>
        public string PhoneNumber { get; set; }
        /// <summary>账号是否启用。</summary>
        public bool IsActive { get; set; }
        /// <summary>是否拥有有效内置管理员角色。</summary>
        public bool IsAdmin { get; set; }
    }

    /// <summary>管理员查询角色时返回的当前状态与并发版本。</summary>
    public sealed class ManagedRoleDto
    {
        /// <summary>角色标识。</summary>
        public Guid Uid { get; set; }
        /// <summary>并发版本。</summary>
        public int Version { get; set; }
        /// <summary>不可变稳定编码。</summary>
        public string RoleCode { get; set; }
        /// <summary>当前显示名称。</summary>
        public string RoleName { get; set; }
        /// <summary>描述。</summary>
        public string Description { get; set; }
        /// <summary>是否启用。</summary>
        public bool IsActive { get; set; }
        /// <summary>是否为内置角色。</summary>
        public bool IsSystem { get; set; }
    }

    /// <summary>权限目录只读行，标明普通角色是否可以分配。</summary>
    public sealed class ManagedPermissionDto
    {
        /// <summary>定义标识。</summary>
        public Guid Uid { get; set; }
        /// <summary>稳定权限键。</summary>
        public string PermissionKey { get; set; }
        /// <summary>显示名称。</summary>
        public string DisplayName { get; set; }
        /// <summary>分组键。</summary>
        public string GroupKey { get; set; }
        /// <summary>组内排序。</summary>
        public int SortOrder { get; set; }
        /// <summary>是否启用。</summary>
        public bool IsActive { get; set; }
        /// <summary>普通角色是否可分配。</summary>
        public bool IsAssignable { get; set; }
    }

    /// <summary>用户角色整组读取结果，版本来自用户聚合。</summary>
    public sealed class UserRolesDto
    {
        /// <summary>用户标识。</summary>
        public Guid UserId { get; set; }
        /// <summary>当前用户版本。</summary>
        public int Version { get; set; }
        /// <summary>有效角色标识集合。</summary>
        public List<Guid> RoleIds { get; set; } = new List<Guid>();
    }

    /// <summary>角色权限整组读取结果；内置管理员的权限来自代码目录。</summary>
    public sealed class RolePermissionsDto
    {
        /// <summary>角色标识。</summary>
        public Guid RoleId { get; set; }
        /// <summary>当前角色版本。</summary>
        public int Version { get; set; }
        /// <summary>权限定义标识集合。</summary>
        public List<Guid> PermissionIds { get; set; } = new List<Guid>();
        /// <summary>内置管理员是否自动获得全部权限。</summary>
        public bool IsAutomatic { get; set; }
    }

    /// <summary>指定目标用户和完整角色集合，并校验用户预期版本。</summary>
    public sealed class ReplaceUserRolesRequest
    {
        /// <summary>用户聚合的预期版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>目标有效角色标识集合；空集合表示全部撤销。</summary>
        public List<Guid> RoleIds { get; set; } = new List<Guid>();
    }

    /// <summary>指定目标普通角色和完整权限集合，并校验角色预期版本。</summary>
    public sealed class ReplaceRolePermissionsRequest
    {
        /// <summary>角色聚合的预期版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>目标可分配权限定义标识集合；空集合表示全部撤销。</summary>
        public List<Guid> PermissionIds { get; set; } = new List<Guid>();
    }

    /// <summary>更新用户资料，不接受密码或权限字段。</summary>
    public sealed class UpdateManagedUserRequest
    {
        /// <summary>用户聚合的预期版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>显示名称。</summary>
        public string NickName { get; set; }
        /// <summary>邮箱。</summary>
        public string Email { get; set; }
        /// <summary>联系电话。</summary>
        public string PhoneNumber { get; set; }
    }

    /// <summary>启停用户或角色时的版本化请求。</summary>
    public sealed class SetManagedStatusRequest
    {
        /// <summary>聚合的预期版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>目标启用状态。</summary>
        public bool IsActive { get; set; }
    }

    /// <summary>创建普通角色；稳定编码创建后不可修改。</summary>
    public sealed class CreateManagedRoleRequest
    {
        /// <summary>不可复用的稳定角色编码。</summary>
        public string RoleCode { get; set; }
        /// <summary>角色显示名称。</summary>
        public string RoleName { get; set; }
        /// <summary>角色描述。</summary>
        public string Description { get; set; }
    }

    /// <summary>编辑角色显示资料，不接受角色编码和系统标志。</summary>
    public sealed class UpdateManagedRoleRequest
    {
        /// <summary>角色聚合的预期版本。</summary>
        public int ExpectedVersion { get; set; }
        /// <summary>新显示名称。</summary>
        public string RoleName { get; set; }
        /// <summary>新描述。</summary>
        public string Description { get; set; }
    }

    /// <summary>按关键词分页查询管理列表。</summary>
    /// <summary>管理员分页查询条件；空状态返回启用与停用的完整结果。</summary>
    public sealed class ManagedPageRequest : PagedRequestBase
    {
        /// <summary>可选启用状态筛选，适用于用户和角色列表。</summary>
        public bool? IsActive { get; set; }
    }
}
