using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>
/// 角色主表实体，对应数据库 [dbo].[SysRoles]。
/// 支持父角色嵌套（ParentRoleId），可构建角色树。
/// </summary>
[Table("SysRoles")]
public partial class SysRole : BaseEntity
{
    /// <summary>角色名称（唯一，数据库有 UQ_Roles 唯一索引）</summary>
    public string RoleName { get; set; } = default!;

    /// <summary>角色描述</summary>
    public string? Description { get; set; }

    /// <summary>父角色 ID（顶级角色为 null，支持角色层级继承）</summary>
    public Guid? ParentRoleId { get; set; }

}

