using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>
/// 用户与角色的授权关系；显示名称由有效角色查询投影提供。
/// </summary>
[Table("SysUser_Roles")]
public partial class SysUserRole : BaseEntity
{
    /// <summary>用户 ID（外键 → SysUser.Uid）</summary>
    public Guid UserId { get; set; }

    /// <summary>角色 ID（外键 → SysRole.Uid，如有角色主表）</summary>
    public Guid RoleId { get; set; }

}
