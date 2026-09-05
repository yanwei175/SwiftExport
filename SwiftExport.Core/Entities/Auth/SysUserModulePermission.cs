using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Auth;

/// <summary>保存普通用户直接获得的一级模块权限，不承载角色或管理员身份。</summary>
public sealed class SysUserModulePermission : BaseEntity
{
    /// <summary>被授予模块的用户标识。</summary>
    public Guid UserId { get; set; }

    /// <summary>代码目录中定义的稳定一级模块键。</summary>
    public string PermissionKey { get; set; } = string.Empty;
}
