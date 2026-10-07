using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>角色与权限定义的授权关系；删除关系后不再贡献有效权限。</summary>
[Table("sys_role_permission")]
public partial class SysRolePermission : BaseEntity
{
    /// <summary>角色标识，逻辑引用 SysRole。</summary>
    public Guid RoleId { get; set; }

    /// <summary>权限定义标识，逻辑引用 SysPermission。</summary>
    public Guid PermissionId { get; set; }
}
