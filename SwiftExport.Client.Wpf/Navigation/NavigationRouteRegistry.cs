using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Products;
using SwiftExportClient.Features.Purchasing;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Shipping;

namespace SwiftExportClient.Navigation;

public sealed class NavigationRouteRegistry : INavigationRouteRegistry
{
    private readonly IReadOnlyList<NavigationRoute> _routes =
    [
        new("home", "首页", "Workspace", typeof(HomeViewModel), CanClose: false),
        new("products", "产品", "Workspace", typeof(ProductsViewModel)),
        new("sales", "销售", "Workspace", typeof(SalesViewModel)),
        new("planning", "计划", "Workspace", typeof(PlanningViewModel)),
        new("purchasing", "采购", "Workspace", typeof(PurchasingViewModel)),
        new("shipping", "出货", "Workspace", typeof(ShippingViewModel)),
    ];

    private readonly Dictionary<string, NavigationRoute> _routeMap;

    public NavigationRouteRegistry()
    {
        _routeMap = _routes.ToDictionary(route => route.Key, StringComparer.OrdinalIgnoreCase);
        MenuItems =
        [
            new("home", "首页", "Workspace", "Home24"),
            new(
                "products",
                "产品",
                "Workspace",
                "Box24",
                IsExpanded: true,
                Children:
                [
                    new("products", "产品资料", "Workspace", "Cube24"),
                    new("product-bom", "BOM", "Workspace", "Branch24"),
                    new("customer-products", "客户产品", "Workspace", "People24"),
                    new("supplier-products", "供应商产品", "Workspace", "PeopleTeam24"),
                ]),
            new("sales", "销售", "Workspace", "ReceiptMoney24"),
            new("planning", "计划", "Workspace", "CalendarLtr24"),
            new("purchasing", "采购", "Workspace", "Cart24"),
            new("shipping", "出货", "VehicleTruckProfile24"),
        ];
    }

    public IReadOnlyList<NavigationRoute> Routes => _routes;

    public IReadOnlyList<NavigationMenuItem> MenuItems { get; }

    public bool TryGetRoute(string routeKey, out NavigationRoute route)
    {
        return _routeMap.TryGetValue(routeKey, out route!);
    }
}
