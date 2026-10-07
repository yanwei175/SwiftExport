using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>
/// 角色主表实体，对应数据库 [dbo].[SysRoles]。
/// 使用稳定编码识别内置角色，不提供角色继承。
/// </summary>
[Table("sys_role")]
public partial class SysRole : BaseEntity
{
    /// <summary>稳定角色编码；内置管理员使用 SYSTEM_ADMIN。</summary>
    public string RoleCode { get; set; } = default!;

    /// <summary>角色显示名称，不参与授权判断。</summary>
    public string RoleName { get; set; } = default!;

    /// <summary>角色描述</summary>
    public string? Description { get; set; }

    /// <summary>角色是否启用；停用角色不贡献权限。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>内置角色标志，普通管理命令不能删除或停用。</summary>
    public bool IsSystem { get; set; }

}

