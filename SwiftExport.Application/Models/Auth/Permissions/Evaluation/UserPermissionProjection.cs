namespace SwiftExport.Application.Models.Auth.Permissions.Evaluation;

/// <summary>表示服务端统一生成的角色与权限快照。</summary>
public sealed class UserPermissionProjection
{
    /// <summary>用户是否拥有有效内置 SYSTEM_ADMIN 角色。</summary>
    public bool IsAdmin { get; init; }

    /// <summary>供当前用户信息展示的有效角色名称。</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>有效角色的稳定编码。</summary>
    public IReadOnlyList<string> RoleCodes { get; init; } = [];

    /// <summary>按代码目录过滤后的有效权限键。</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
