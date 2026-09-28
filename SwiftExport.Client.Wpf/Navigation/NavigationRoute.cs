namespace SwiftExportClient.Navigation;

/// <summary>描述页签路由、所属模块及进入该页面所需的稳定权限键。</summary>
/// <param name="AnyRequiredPermissions">可选的任一权限集合；提供时必须至少拥有一项，且同时满足 RequiredPermission。</param>
/// <param name="DiscardOnAccessRevoked">撤销入口权限时是否立即清理敏感草稿；普通业务页默认保留草稿等待关闭。</param>
public sealed record NavigationRoute(
    string Key,
    string Title,
    string Group,
    Type ViewModelType,
    string ModuleKey,
    string? RequiredPermission,
    bool CanClose = true,
    IReadOnlyList<string>? AnyRequiredPermissions = null,
    bool DiscardOnAccessRevoked = false);
