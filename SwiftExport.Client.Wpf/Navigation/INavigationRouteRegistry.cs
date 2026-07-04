namespace SwiftExportClient.Navigation;

public interface INavigationRouteRegistry
{
    IReadOnlyList<NavigationRoute> Routes { get; }

    IReadOnlyList<NavigationMenuItem> MenuItems { get; }

    bool TryGetRoute(string routeKey, out NavigationRoute route);
}
