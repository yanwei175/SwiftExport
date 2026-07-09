using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>
/// 用户-角色关联表实体，对应数据库 [dbo].[SysUser_Roles]。
/// 一条记录表示某用户拥有某角色。
/// </summary>
[Table("SysUser_Roles")]
public class SysUserRole : BaseEntity
{
    /// <summary>用户 ID（外键 → SysUser.Uid）</summary>
    public Guid UserId { get; set; }

    /// <summary>用户名（冗余字段，方便查询展示，无需 Join）</summary>
    public string? UserName { get; set; }

    /// <summary>角色 ID（外键 → SysRole.Uid，如有角色主表）</summary>
    public Guid RoleId { get; set; }

    /// <summary>角色名称（冗余字段，方便查询展示）</summary>
    public string? RoleName { get; set; }
}
