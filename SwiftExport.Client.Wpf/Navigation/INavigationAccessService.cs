namespace SwiftExportClient.Navigation;

/// <summary>依据当前登录用户的权限快照统一判断导航路由是否可进入。</summary>
public interface INavigationAccessService
{
    /// <summary>判断已注册路由是否允许当前用户访问。</summary>
    bool CanAccess(NavigationRoute route);

    /// <summary>解析路由键并判断是否允许当前用户访问；未知路由始终拒绝。</summary>
    bool CanAccess(string routeKey);
}
