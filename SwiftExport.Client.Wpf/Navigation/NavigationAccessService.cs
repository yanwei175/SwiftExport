using SwiftExportClient.Authentication;

namespace SwiftExportClient.Navigation;

/// <summary>以 me 返回的 Permissions 快照实施统一导航门禁，不使用角色名兜底。</summary>
public sealed class NavigationAccessService(
    INavigationRouteRegistry routeRegistry,
    ICurrentUserService currentUserService) : INavigationAccessService
{
    /// <inheritdoc />
    public bool CanAccess(NavigationRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return currentUserService.IsAuthenticated
            && !string.IsNullOrWhiteSpace(route.ModuleKey)
            && (route.RequiredPermission is null
                || currentUserService.HasPermission(route.RequiredPermission))
            && (route.AnyRequiredPermissions is null
                || route.AnyRequiredPermissions.Any(currentUserService.HasPermission));
    }

    /// <inheritdoc />
    public bool CanAccess(string routeKey)
    {
        return !string.IsNullOrWhiteSpace(routeKey)
            && routeRegistry.TryGetRoute(routeKey, out var route)
            && CanAccess(route);
    }
}
