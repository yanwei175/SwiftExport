using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Placeholders.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Fulfillment.ViewModels;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;

namespace SwiftExportClient.Navigation;

/// <summary>集中定义客户端路由、权限和侧边菜单结构。</summary>
public sealed class NavigationRouteRegistry : INavigationRouteRegistry
{
    private readonly IReadOnlyList<NavigationRoute> _routes =
    [
        new("home", "首页", "Workspace", typeof(HomeViewModel), NavigationModuleKeys.Home, RequiredPermission: null, CanClose: false),
        new("products", "产品", "Workspace", typeof(ProductsViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new(ProductMasterManagerViewModel.RouteKey, "主产品管理", "Workspace", typeof(ProductMasterManagerViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new(ProductNeutralManagerViewModel.RouteKey, "中性产品管理", "Workspace", typeof(ProductNeutralManagerViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new(ProductMasterDetailViewModel.RouteKey, "主型号详情", "Workspace", typeof(ProductMasterDetailViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new("product-category-manager", "产品分类管理", "Workspace", typeof(ProductCategoryManagerViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new("product-colors", "产品颜色管理", "Workspace", typeof(ProductColorsViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new("product-bom", "BOM", "Workspace", typeof(ProductBomViewModel), NavigationModuleKeys.Products, PermissionKeys.ModuleProductsAccess),
        new("customers", "客户管理", "Workspace", typeof(CustomerManagerViewModel), NavigationModuleKeys.Customers, PermissionKeys.ModuleCustomersAccess),
        new(CustomerPoConfigManagerViewModel.RouteKey, "客户采购默认配置", "Workspace", typeof(CustomerPoConfigManagerViewModel), NavigationModuleKeys.Customers, PermissionKeys.ModuleCustomersAccess),
        new(CustomerProductWorkspaceViewModel.RouteKey, "客户产品管理", "Workspace", typeof(CustomerProductWorkspaceViewModel), NavigationModuleKeys.Customers, PermissionKeys.ModuleCustomersAccess),
        new("suppliers", "供应商管理", "Workspace", typeof(SupplierManagerViewModel), NavigationModuleKeys.Suppliers, PermissionKeys.ModuleSuppliersAccess),
        new(SupplierProductWorkspaceViewModel.RouteKey, "供应商产品管理", "Workspace", typeof(SupplierProductWorkspaceViewModel), NavigationModuleKeys.Suppliers, PermissionKeys.ModuleSuppliersAccess),
        new("sales", "销售", "Workspace", typeof(SalesViewModel), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
        new(SalesOrderManagerViewModel.RouteKey, "销售订单管理", "Workspace", typeof(SalesOrderManagerViewModel), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
        new(SalesOrderManagerViewModel.DetailRouteKey, "销售订单", "Workspace", typeof(SalesOrderDetailViewModel), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
        new(SalesFulfillmentManagerViewModel.RouteKey, "销售履约查询", "Manager", typeof(SalesFulfillmentManagerViewModel), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
        new(SalesFulfillmentDetailViewModel.RouteKey, "销售履约详情", "Detail", typeof(SalesFulfillmentDetailViewModel), NavigationModuleKeys.Sales, PermissionKeys.ModuleSalesAccess),
        new(GrossDemandWorkspaceViewModel.RouteKey, "毛需求", "Workspace", typeof(GrossDemandWorkspaceViewModel), NavigationModuleKeys.DemandPlanning, PermissionKeys.ModuleDemandPlanningAccess),
        new(NetDemandManagerViewModel.RouteKey, "净需求", "Workspace", typeof(NetDemandManagerViewModel), NavigationModuleKeys.DemandPlanning, PermissionKeys.ModuleDemandPlanningAccess),
        new(PurchaseOrderManagerViewModel.RouteKey, "采购订单管理", "Workspace", typeof(PurchaseOrderManagerViewModel), NavigationModuleKeys.Procurement, PermissionKeys.ModuleProcurementAccess),
        new(PurchaseOrderProgressWorkspaceViewModel.RouteKey, "采购订单跟进", "Workspace", typeof(PurchaseOrderProgressWorkspaceViewModel), NavigationModuleKeys.Procurement, PermissionKeys.ModuleProcurementAccess),
        new(PurchaseOrderDetailViewModel.RouteKey, "采购单详情", "Workspace", typeof(PurchaseOrderDetailViewModel), NavigationModuleKeys.Procurement, PermissionKeys.ModuleProcurementAccess),
        new(AfterSalesManagerViewModel.RouteKey, "售后服务管理", "Manager", typeof(AfterSalesManagerViewModel), NavigationModuleKeys.QualityControl, PermissionKeys.ModuleQualityControlAccess),
        new(AfterSalesDetailViewModel.RouteKey, "售后服务单", "Detail", typeof(AfterSalesDetailViewModel), NavigationModuleKeys.QualityControl, PermissionKeys.ModuleQualityControlAccess),
        new(WarehouseSitesViewModel.RouteKey, "供应商虚拟仓", "Workspace", typeof(WarehouseSitesViewModel), NavigationModuleKeys.Inventory, PermissionKeys.ModuleInventoryAccess),
        new(InventoryBalancesViewModel.RouteKey, "库存余额", "Workspace", typeof(InventoryBalancesViewModel), NavigationModuleKeys.Inventory, PermissionKeys.ModuleInventoryAccess),
        new(InboundManagerViewModel.RouteKey, "入库单管理", "Workspace", typeof(InboundManagerViewModel), NavigationModuleKeys.Inventory, PermissionKeys.ModuleInventoryAccess),
        new(OutboundManagerViewModel.RouteKey, "出库单管理", "Workspace", typeof(OutboundManagerViewModel), NavigationModuleKeys.Inventory, PermissionKeys.ModuleInventoryAccess),
        new(InventoryLedgerViewModel.RouteKey, "库存流水", "Workspace", typeof(InventoryLedgerViewModel), NavigationModuleKeys.Inventory, PermissionKeys.ModuleInventoryAccess),
        new("planning", "计划", "Workspace", typeof(PlanningViewModel), NavigationModuleKeys.DemandPlanning, PermissionKeys.ModuleDemandPlanningAccess),
        new(SettingsCenterViewModel.RouteKey, "设置", "Workspace", typeof(SettingsCenterViewModel), NavigationModuleKeys.SettingsCenter, RequiredPermission: null),
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
                IsExpanded: false,
                Children:
                [
                    new(ProductMasterManagerViewModel.RouteKey, "主产品", "Workspace", "DocumentTable24"),
                    new(ProductNeutralManagerViewModel.RouteKey, "中性产品", "Workspace", "DocumentTable24"),
                    new("products", "物料", "Workspace", "Cube24"),
                    new("product-category-manager", "产品分类", "Workspace", "Folder24"),
                    new("product-colors", "产品颜色", "Workspace", "Color24"),
                    new("product-bom", "BOM", "Workspace", "Branch24"),
                ]),
            new(
                "customers",
                "客户",
                "Workspace",
                "People24",
                IsExpanded: false,
                Children:
                [
                    new("customers", "客户", "Workspace", "People24"),
                    new(CustomerPoConfigManagerViewModel.RouteKey, "采购默认配置", "Workspace", "Settings24"),
                    new(CustomerProductWorkspaceViewModel.RouteKey, "客户产品管理", "Workspace", "DocumentTable24"),
                ]),
            new(
                "suppliers",
                "供应商",
                "Workspace",
                "PeopleTeam24",
                IsExpanded: false,
                Children:
                [
                    new("suppliers", "供应商清单", "Workspace", "PeopleTeam24"),
                    new(SupplierProductWorkspaceViewModel.RouteKey, "供应商产品", "Workspace", "DocumentTable24"),
                ]),
            new(
                "sales",
                "销售",
                "Workspace",
                "ReceiptMoney24",
                IsExpanded: false,
                Children:
                [
                    new(SalesOrderManagerViewModel.RouteKey, "销售订单管理", "Workspace", "DocumentBulletList24"),
                    new(SalesFulfillmentManagerViewModel.RouteKey, "销售履约查询", "Manager", "DataTrending24"),
                ]),
            new(
                "demand-planning",
                "需求计划",
                "Workspace",
                "CalendarLtr24",
                IsExpanded: false,
                Children:
                [
                    new(GrossDemandWorkspaceViewModel.RouteKey, "来源需求", "Workspace", "DataTrending24"),
                    new(NetDemandManagerViewModel.RouteKey, "物料需求", "Workspace", "ClipboardTaskListLtr24"),
                ]),
            new(
                "procurement",
                "采购",
                "Workspace",
                "Cart24",
                IsExpanded: false,
                Children:
                [
                    new(PurchaseOrderManagerViewModel.RouteKey, "采购订单管理", "Workspace", "DocumentBulletList24"),
                    new(PurchaseOrderProgressWorkspaceViewModel.RouteKey, "采购订单跟进", "Workspace", "ClipboardTaskListLtr24"),
                ]),
            new(
                NavigationModuleKeys.QualityControl,
                "品质管理",
                "Workspace",
                "ShieldCheckmark24",
                IsExpanded: false,
                Children:
                [
                    new(AfterSalesManagerViewModel.RouteKey, "售后服务管理", "Manager", "ClipboardTaskListLtr24"),
                ]),
            new(
                "inventory",
                "库存",
                "Workspace",
                "Database24",
                IsExpanded: false,
                Children:
                [
                    new(WarehouseSitesViewModel.RouteKey, "供应商虚拟仓", "Workspace", "BuildingRetail24"),
                    new(InventoryBalancesViewModel.RouteKey, "库存余额", "Workspace", "DataBarHorizontal24"),
                    new(InboundManagerViewModel.RouteKey, "入库单管理", "Workspace", "ArrowDownload24"),
                    new(OutboundManagerViewModel.RouteKey, "出库单管理", "Workspace", "ArrowUpload24"),
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
