using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>用户角色查询实体，汇总用户、设备和角色信息。</summary>
public class UserRoleView : BaseEntity
{
    /// <summary>该实体保存的登录账号。</summary>
    public string UserName { get; set; } = default!;
    /// <summary>该实体保存的登录设备名称。</summary>
    public string? PcName { get; set; }
    /// <summary>该实体保存的登录密码凭据。</summary>
    public string Password { get; set; } = default!;
    /// <summary>该实体保存的用户显示名称。</summary>
    public string? NickName { get; set; }
    /// <summary>关联的系统角色唯一标识。</summary>
    public int RoleID { get; set; } = default!;
    /// <summary>该实体保存的角色名称。</summary>
    public string? RoleName { get; set; }
}
