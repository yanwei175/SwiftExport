using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Placeholders.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.QualityControl.Categories.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Fulfillment.ViewModels;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;

namespace SwiftExportClient.Navigation;

/// <summary>集中定义客户端路由、权限和侧边菜单结构。</summary>
public sealed class NavigationRouteRegistry : INavigationRouteRegistry
{
    private readonly IReadOnlyList<NavigationRoute> _routes =
    [

        new("home", "首页", "Workspace", typeof(HomeViewModel), NavigationModuleKeys.Home, RequiredPermission: null, CanClose: false),

        new(PackingListManagerViewModel.RouteKey, "PackingList工作台", "Workspace", typeof(PackingListManagerViewModel), NavigationModuleKeys.Shipping, "Shipping.PackingList.View"),

        new(PackingListDetailViewModel.RouteKey, "PackingList详情", "Detail", typeof(PackingListDetailViewModel), NavigationModuleKeys.Shipping, "Shipping.PackingList.View"),

        new("products", "产品", "Workspace", typeof(ProductsViewModel), NavigationModuleKeys.Products, "Products.Product.View"),

        new(ProductMasterManagerViewModel.RouteKey, "主产品管理", "Workspace", typeof(ProductMasterManagerViewModel), NavigationModuleKeys.Products, "Products.Master.View"),

        new(ProductNeutralManagerViewModel.RouteKey, "中性产品管理", "Workspace", typeof(ProductNeutralManagerViewModel), NavigationModuleKeys.Products, "Products.Neutral.View"),

        new(ProductMasterDetailViewModel.RouteKey, "主型号详情", "Workspace", typeof(ProductMasterDetailViewModel), NavigationModuleKeys.Products, "Products.Master.View"),

        new("product-category-manager", "产品分类管理", "Workspace", typeof(ProductCategoryManagerViewModel), NavigationModuleKeys.Products, "Products.Category.View"),

        new("product-colors", "产品颜色管理", "Workspace", typeof(ProductColorsViewModel), NavigationModuleKeys.Products, "Products.Color.View"),

        new("product-bom", "BOM", "Workspace", typeof(ProductBomViewModel), NavigationModuleKeys.Products, "Products.Bom.View"),

        new("customers", "客户管理", "Workspace", typeof(CustomerManagerViewModel), NavigationModuleKeys.Customers, "Customers.Customer.View"),

        new(CustomerPoConfigManagerViewModel.RouteKey, "客户采购默认配置", "Workspace", typeof(CustomerPoConfigManagerViewModel), NavigationModuleKeys.Customers, "Customers.PoConfig.View"),

        new(CustomerProductWorkspaceViewModel.RouteKey, "客户产品管理", "Workspace", typeof(CustomerProductWorkspaceViewModel), NavigationModuleKeys.Customers, "Customers.Products.View"),

        new("suppliers", "供应商管理", "Workspace", typeof(SupplierManagerViewModel), NavigationModuleKeys.Suppliers, "Suppliers.Supplier.View"),

        new(SupplierProductWorkspaceViewModel.RouteKey, "供应商产品管理", "Workspace", typeof(SupplierProductWorkspaceViewModel), NavigationModuleKeys.Suppliers, "Suppliers.Products.View"),

        new("sales", "销售", "Workspace", typeof(SalesViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(SalesOrderManagerViewModel.RouteKey, "销售订单管理", "Workspace", typeof(SalesOrderManagerViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(SalesOrderManagerViewModel.DetailRouteKey, "销售订单", "Workspace", typeof(SalesOrderDetailViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(SalesFulfillmentManagerViewModel.RouteKey, "销售履约查询", "Manager", typeof(SalesFulfillmentManagerViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(SalesFulfillmentDetailViewModel.RouteKey, "销售履约详情", "Detail", typeof(SalesFulfillmentDetailViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(GrossDemandWorkspaceViewModel.RouteKey, "成品需求", "Workspace", typeof(GrossDemandWorkspaceViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Source.View"),
        new(GrossDemandTraceViewModel.RouteKey, "来源需求追踪", "Detail", typeof(GrossDemandTraceViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Source.View"),

        new(NetDemandManagerViewModel.RouteKey, "物料需求", "Workspace", typeof(NetDemandManagerViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Material.View"),
        new(MaterialDemandTraceViewModel.RouteKey, "物料需求追踪", "Detail", typeof(MaterialDemandTraceViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Material.View"),

        new(PurchaseOrderManagerViewModel.RouteKey, "采购订单管理", "Workspace", typeof(PurchaseOrderManagerViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Read"),
        new(PurchaseDemandAssociationViewModel.RouteKey, "关联物料需求", "Workspace", typeof(PurchaseDemandAssociationViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Edit", AnyRequiredPermissions: [PermissionKeys.ProcurementOrderRead]),
        new(PurchaseOrderTraceViewModel.RouteKey, "采购追溯", "Detail", typeof(PurchaseOrderTraceViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Read"),

        new(PurchaseOrderProgressWorkspaceViewModel.RouteKey, "采购订单跟进", "Workspace", typeof(PurchaseOrderProgressWorkspaceViewModel), NavigationModuleKeys.Procurement, "Procurement.Progress.View"),

        new(PurchaseOrderDetailViewModel.RouteKey, "采购单详情", "Workspace", typeof(PurchaseOrderDetailViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Read"),

        new(AfterSalesManagerViewModel.RouteKey, "售后服务管理", "Manager", typeof(AfterSalesManagerViewModel), NavigationModuleKeys.QualityControl, "QualityControl.AfterSales.View"),

        new(AfterSalesQualityCategoryManagerViewModel.RouteKey, "售后品质分类", "Workspace", typeof(AfterSalesQualityCategoryManagerViewModel), NavigationModuleKeys.QualityControl, PermissionKeys.QualityControlAfterSalesView),

        new(AfterSalesDetailViewModel.RouteKey, "售后服务单", "Detail", typeof(AfterSalesDetailViewModel), NavigationModuleKeys.QualityControl, "QualityControl.AfterSales.View"),

        new(WarehouseSitesViewModel.RouteKey, "供应商虚拟仓", "Workspace", typeof(WarehouseSitesViewModel), NavigationModuleKeys.Inventory, "Inventory.Warehouse.View"),

        new(InventoryBalancesViewModel.RouteKey, "库存余额", "Workspace", typeof(InventoryBalancesViewModel), NavigationModuleKeys.Inventory, "Inventory.Balance.View"),

        new(InboundManagerViewModel.RouteKey, "入库单管理", "Workspace", typeof(InboundManagerViewModel), NavigationModuleKeys.Inventory, "Inventory.Inbound.View"),

        new(OutboundManagerViewModel.RouteKey, "出库单管理", "Workspace", typeof(OutboundManagerViewModel), NavigationModuleKeys.Inventory, "Inventory.Outbound.View"),

        new(InventoryLedgerViewModel.RouteKey, "库存流水", "Workspace", typeof(InventoryLedgerViewModel), NavigationModuleKeys.Inventory, "Inventory.Balance.View"),

        new("planning", "计划", "Workspace", typeof(PlanningViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Source.View"),

        new(SettingsPageViewModel.RouteKey, "设置", "Workspace",
            typeof(SettingsPageViewModel), NavigationModuleKeys.SystemSettings, RequiredPermission: null,
            DiscardOnAccessRevoked: true),
    ];

    private readonly Dictionary<string, NavigationRoute> _routeMap;

    /// <summary>构建稳定路由索引及侧边菜单，权限沿用各业务用例。</summary>
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
                    new(ProductMasterManagerViewModel.RouteKey, "父级型号", "Workspace", "DocumentTable24"),
                    new(ProductNeutralManagerViewModel.RouteKey, "产品管理", "Workspace", "DocumentTable24"),
                    new("products", "物料管理", "Workspace", "Cube24"),
                    new("product-category-manager", "产品分类", "Workspace", "Folder24"),
                    new("product-colors", "产品颜色", "Workspace", "Color24"),
                    new("product-bom", "物料清单", "Workspace", "Branch24"),
                ]),
            new(
                "customers",
                "客户",
                "Workspace",
                "People24",
                IsExpanded: false,
                Children:
                [
                    new("customers", "客户管理", "Workspace", "People24"),
                    new(CustomerProductWorkspaceViewModel.RouteKey, "客户产品", "Workspace", "DocumentTable24"),
                    new(CustomerPoConfigManagerViewModel.RouteKey, "客户专用单头", "Workspace", "Settings24"),
                ]),
            new(
                "suppliers",
                "供应商",
                "Workspace",
                "PeopleTeam24",
                IsExpanded: false,
                Children:
                [
                    new("suppliers", "供应商管理", "Workspace", "PeopleTeam24"),
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
                    new(SalesOrderManagerViewModel.RouteKey, "销售订单", "Workspace", "DocumentBulletList24"),
                ]),
            new(
                "demand-planning",
                "需求计划",
                "Workspace",
                "CalendarLtr24",
                IsExpanded: false,
                Children:
                [
                    new(GrossDemandWorkspaceViewModel.RouteKey, "成品需求", "Workspace", "DataTrending24"),
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
                    new(PurchaseOrderManagerViewModel.RouteKey, "采购订单", "Workspace", "DocumentBulletList24"),
                    new(PurchaseDemandAssociationViewModel.RouteKey, "关联物料需求", "Workspace", "Branch24"),
                    new(PurchaseOrderProgressWorkspaceViewModel.RouteKey, "采购跟进", "Workspace", "ClipboardTaskListLtr24"),
                ]),
            new(
                NavigationModuleKeys.QualityControl,
                "品管",
                "Workspace",
                "ShieldCheckmark24",
                IsExpanded: false,
                Children:
                [
                    new(AfterSalesManagerViewModel.RouteKey, "售后投诉", "Manager", "ClipboardTaskListLtr24"),
                    new(AfterSalesQualityCategoryManagerViewModel.RouteKey, "品质分类", "Workspace", "Folder24"),
                ]),
            new(
                "inventory",
                "仓库",
                "Workspace",
                "Database24",
                IsExpanded: false,
                Children:
                [
                    new(WarehouseSitesViewModel.RouteKey, "仓库", "Workspace", "BuildingRetail24"),
                    new(InventoryBalancesViewModel.RouteKey, "库存", "Workspace", "DataBarHorizontal24"),
                    new(InboundManagerViewModel.RouteKey, "入库单", "Workspace", "ArrowDownload24"),
                    new(OutboundManagerViewModel.RouteKey, "出库单", "Workspace", "ArrowUpload24"),
                    new(InventoryLedgerViewModel.RouteKey, "流水", "Workspace", "ClipboardTextLtr24"),
                ]),
            new(NavigationModuleKeys.Shipping, "出货", "Workspace", "ArrowUpload24", IsExpanded: false,
                Children: [new(PackingListManagerViewModel.RouteKey, "PackingList管理", "Workspace", "DocumentBulletList24")]),
        ];
    }

    public IReadOnlyList<NavigationRoute> Routes => _routes;

    public IReadOnlyList<NavigationMenuItem> MenuItems { get; }

    public bool TryGetRoute(string routeKey, out NavigationRoute route)
    {
        return _routeMap.TryGetValue(routeKey, out route!);
    }
}
