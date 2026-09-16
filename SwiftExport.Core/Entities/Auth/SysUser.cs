using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>系统用户实体，保存登录身份、账号状态和用户资料。</summary>
[Table("SysUser")]
public partial class SysUser : BaseEntity
{
    /// <summary>登录用户名（唯一）</summary>
    public string UserName { get; set; } = default!;

    /// <summary>显示昵称</summary>
    public string? NickName { get; set; }

    /// <summary>BCrypt 哈希后的密码，禁止明文存储</summary>
    public string PasswordHash { get; set; } = default!;

    /// <summary>凭据版本；密码改变时递增，用于使此前签发的 JWT 失效。</summary>
    public int CredentialVersion { get; set; } = 1;

    /// <summary>邮箱</summary>
    public string? Email { get; set; }

    /// <summary>手机号</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;
}

