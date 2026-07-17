using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Placeholders.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;

namespace SwiftExportClient.Navigation;

public sealed class NavigationRouteRegistry : INavigationRouteRegistry
{
    private readonly IReadOnlyList<NavigationRoute> _routes =
    [
        new("home", "首页", "Workspace", typeof(HomeViewModel), CanClose: false),
        new("products", "产品", "Workspace", typeof(ProductsViewModel)),
        new("product-category-manager", "产品分类管理", "Workspace", typeof(ProductCategoryManagerViewModel)),
        new("product-colors", "产品颜色管理", "Workspace", typeof(ProductColorsViewModel)),
        new("product-bom", "BOM", "Workspace", typeof(ProductBomViewModel)),
        new("customers", "客户管理", "Workspace", typeof(CustomerManagerViewModel)),
        new("suppliers", "供应商管理", "Workspace", typeof(SupplierManagerViewModel)),
        new("sales", "销售", "Workspace", typeof(SalesViewModel)),
        new(SalesOrderManagerViewModel.ManagerRouteKey, "销售订单管理", "Workspace", typeof(SalesOrderManagerViewModel)),
        new(SalesOrderManagerViewModel.DetailRouteKey, "销售订单", "Workspace", typeof(SalesOrderDetailViewModel)),
        new(GrossDemandWorkspaceViewModel.RouteKey, "毛需求", "Workspace", typeof(GrossDemandWorkspaceViewModel)),
        new(NetDemandWorkspaceViewModel.RouteKey, "净需求", "Workspace", typeof(NetDemandWorkspaceViewModel)),
        new(PurchaseOrderWorkspaceViewModel.RouteKey, "采购工作台", "Workspace", typeof(PurchaseOrderWorkspaceViewModel)),
        new(PurchaseOrderDetailViewModel.RouteKey, "采购单详情", "Workspace", typeof(PurchaseOrderDetailViewModel)),
        new(WarehouseSitesViewModel.RouteKey, "供应商虚拟仓", "Workspace", typeof(WarehouseSitesViewModel)),
        new(InventoryBalancesViewModel.RouteKey, "库存余额", "Workspace", typeof(InventoryBalancesViewModel)),
        new(InventoryLedgerViewModel.RouteKey, "库存流水", "Workspace", typeof(InventoryLedgerViewModel)),
        new("planning", "计划", "Workspace", typeof(PlanningViewModel)),
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
                    new("product-category-manager", "产品分类管理", "Workspace", "Folder24"),
                    new("product-colors", "产品颜色管理", "Workspace", "Color24"),
                    new("product-bom", "BOM", "Workspace", "Branch24"),
                ]),
            new("customers", "客户管理", "Workspace", "People24"),
            new("suppliers", "供应商管理", "Workspace", "PeopleTeam24"),
            new(SalesOrderManagerViewModel.ManagerRouteKey, "销售订单", "Workspace", "ReceiptMoney24"),
            new(
                "demand-planning",
                "需求计划",
                "Workspace",
                "CalendarLtr24",
                IsExpanded: true,
                Children:
                [
                    new(GrossDemandWorkspaceViewModel.RouteKey, "毛需求", "Workspace", "DataTrending24"),
                    new(NetDemandWorkspaceViewModel.RouteKey, "净需求", "Workspace", "ClipboardTaskListLtr24"),
                ]),
            new(
                "procurement",
                "采购",
                "Workspace",
                "Cart24",
                IsExpanded: true,
                Children:
                [
                    new(PurchaseOrderWorkspaceViewModel.RouteKey, "采购工作台", "Workspace", "DocumentBulletList24"),
                ]),
            new(
                "inventory",
                "库存",
                "Workspace",
                "Database24",
                IsExpanded: true,
                Children:
                [
                    new(WarehouseSitesViewModel.RouteKey, "供应商虚拟仓", "Workspace", "BuildingRetail24"),
                    new(InventoryBalancesViewModel.RouteKey, "库存余额", "Workspace", "DataBarHorizontal24"),
                    new(InventoryLedgerViewModel.RouteKey, "库存流水", "Workspace", "ClipboardTextLtr24"),
                ]),
        ];
    }

    public IReadOnlyList<NavigationRoute> Routes => _routes;

    public IReadOnlyList<NavigationMenuItem> MenuItems { get; }

    public bool TryGetRoute(string routeKey, out NavigationRoute route)
    {
        return _routeMap.TryGetValue(routeKey, out route!);
    }
}
