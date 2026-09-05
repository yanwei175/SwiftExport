namespace SwiftExportClient.Navigation;

/// <summary>描述页签路由、所属模块及进入该页面所需的稳定权限键。</summary>
public sealed record NavigationRoute(
    string Key,
    string Title,
    string Group,
    Type ViewModelType,
    string ModuleKey,
    string? RequiredPermission,
    bool CanClose = true);
