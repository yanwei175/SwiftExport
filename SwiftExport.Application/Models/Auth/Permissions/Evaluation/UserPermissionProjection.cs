namespace SwiftExport.Application.Models.Auth.Permissions.Evaluation;

/// <summary>表示服务端统一生成的角色与权限快照。</summary>
public sealed class UserPermissionProjection
{
    /// <summary>用户是否为真实有效 Admin。</summary>
    public bool IsAdmin { get; init; }

    /// <summary>供当前用户信息展示的有效角色名称。</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>包含模块、管理能力和兼容细权限的最终权限键。</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
