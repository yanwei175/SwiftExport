using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.OrderTracking.Overview.ViewModels;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.MaterialDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Products.Attributes.ViewModels;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductMasterImport.ViewModels;
using SwiftExportClient.Features.Products.ProductNeutralImport.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.QualityControl.Categories.ViewModels;
using SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;

namespace SwiftExportClient.Navigation;

/// <summary>集中定义客户端路由、权限和侧边菜单结构。</summary>
public sealed class NavigationRouteRegistry : INavigationRouteRegistry
{
    private readonly IReadOnlyList<NavigationRoute> _routes =
    [

        new("home", "首页", "Workspace", typeof(HomeViewModel), NavigationModuleKeys.Home, RequiredPermission: null, CanClose: false),

        new(PackingListManagerViewModel.RouteKey, "箱单管理", "Workspace", typeof(PackingListManagerViewModel), NavigationModuleKeys.Shipping, "Shipping.PackingList.View"),

        new(PackingListDetailViewModel.RouteKey, "箱单详情", "Detail", typeof(PackingListDetailViewModel), NavigationModuleKeys.Shipping, "Shipping.PackingList.View"),

        new("products", "物料管理", "Workspace", typeof(ProductsViewModel), NavigationModuleKeys.Products, "Products.Product.View"),
        new(SwiftExportClient.Features.Products.PackagingMaterials.ViewModels.PackagingMaterialManagerViewModel.RouteKey, "包材管理", "Workspace", typeof(SwiftExportClient.Features.Products.PackagingMaterials.ViewModels.PackagingMaterialManagerViewModel), NavigationModuleKeys.Products, "Products.Product.View"),

        new(ProductDetailViewModel.RouteKey, "物料详情", "Detail", typeof(ProductDetailViewModel), NavigationModuleKeys.Products, PermissionKeys.ProductsProductView),

        new(ProductMasterManagerViewModel.RouteKey, "主产品管理", "Workspace", typeof(ProductMasterManagerViewModel), NavigationModuleKeys.Products, "Products.Master.View"),

        new(ProductMasterImportViewModel.RouteKey, "父级型号批量导入", "Workspace", typeof(ProductMasterImportViewModel), NavigationModuleKeys.Products, "Products.Master.View"),
        new(ProductNeutralImportViewModel.RouteKey, "中性产品批量导入", "Workspace", typeof(ProductNeutralImportViewModel), NavigationModuleKeys.Products, PermissionKeys.ProductsNeutralView),

        new(ProductMasterDetailViewModel.RouteKey, "主型号详情", "Workspace", typeof(ProductMasterDetailViewModel), NavigationModuleKeys.Products, "Products.Master.View"),

        new(ProductNeutralManagerViewModel.RouteKey, "中性产品管理", "Workspace", typeof(ProductNeutralManagerViewModel), NavigationModuleKeys.Products, "Products.Neutral.View"),


        new("product-category-manager", "产品分类管理", "Workspace", typeof(ProductCategoryManagerViewModel), NavigationModuleKeys.Products, "Products.Category.View"),

        new(ProductAttributeManagerViewModel.RouteKey, "自定义属性", "Workspace", typeof(ProductAttributeManagerViewModel), NavigationModuleKeys.Products, PermissionKeys.ProductsCategoryView),

        new(DictionaryDataManagerViewModel.RouteKey, "字典明细", "Workspace", typeof(DictionaryDataManagerViewModel), NavigationModuleKeys.SystemSettings, "System.Dictionary.View", DiscardOnAccessRevoked: true),

        new("product-colors", "产品颜色管理", "Workspace", typeof(ProductColorsViewModel), NavigationModuleKeys.Products, "Products.Color.View"),

        new("product-bom", "BOM 管理", "Workspace", typeof(BomManagerViewModel), NavigationModuleKeys.Products, "Products.Bom.View"),

        new("customers", "客户管理", "Workspace", typeof(CustomerManagerViewModel), NavigationModuleKeys.Customers, "Customers.Customer.View"),

        new(CustomerPoConfigManagerViewModel.RouteKey, "客户采购默认配置", "Workspace", typeof(CustomerPoConfigManagerViewModel), NavigationModuleKeys.Customers, "Customers.PoConfig.View"),

        new(CustomerProductManagerViewModel.RouteKey, "客户产品管理", "Workspace", typeof(CustomerProductManagerViewModel), NavigationModuleKeys.Customers, "Customers.Products.View"),

        new("suppliers", "供应商管理", "Workspace", typeof(SupplierManagerViewModel), NavigationModuleKeys.Suppliers, "Suppliers.Supplier.View"),

        new(SupplierProductManagerViewModel.RouteKey, "供应商产品管理", "Workspace", typeof(SupplierProductManagerViewModel), NavigationModuleKeys.Suppliers, "Suppliers.Products.View"),

        new("sales", "销售", "Workspace", typeof(SalesViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(SalesOrderManagerViewModel.RouteKey, "销售订单管理", "Workspace", typeof(SalesOrderManagerViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(SalesOrderManagerViewModel.DetailRouteKey, "销售订单", "Workspace", typeof(SalesOrderDetailViewModel), NavigationModuleKeys.Sales, "Sales.Order.View"),

        new(OrderTrackingOverviewViewModel.RouteKey, "跟踪总览", "Workspace", typeof(OrderTrackingOverviewViewModel), NavigationModuleKeys.OrderTracking, PermissionKeys.OrderTrackingView, DiscardOnAccessRevoked: true),
        new(PurchaseOrderTrackingViewModel.RouteKey, "采购订单跟踪", "Workspace", typeof(PurchaseOrderTrackingViewModel), NavigationModuleKeys.OrderTracking, PermissionKeys.OrderTrackingView, AnyRequiredPermissions: [PermissionKeys.ProcurementOrderRead], DiscardOnAccessRevoked: true),
        new(DemandTrackingDetailViewModel.RouteKey, "成品需求跟踪明细", "Detail", typeof(DemandTrackingDetailViewModel), NavigationModuleKeys.OrderTracking, PermissionKeys.OrderTrackingView, DiscardOnAccessRevoked: true),
        new(MaterialTrackingDetailViewModel.RouteKey, "物料跟踪详情", "Detail", typeof(MaterialTrackingDetailViewModel), NavigationModuleKeys.OrderTracking, PermissionKeys.OrderTrackingView, AnyRequiredPermissions: [PermissionKeys.DemandPlanningMaterialView], DiscardOnAccessRevoked: true),



        new(GrossDemandWorkspaceViewModel.RouteKey, "成品需求", "Workspace", typeof(GrossDemandWorkspaceViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Source.View"),

        new(NetDemandManagerViewModel.RouteKey, "物料需求", "Workspace", typeof(NetDemandManagerViewModel), NavigationModuleKeys.DemandPlanning, "DemandPlanning.Material.View"),

        new(PurchaseOrderManagerViewModel.RouteKey, "采购订单管理", "Workspace", typeof(PurchaseOrderManagerViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Read"),
        new(PurchaseDemandAssociationViewModel.RouteKey, "关联物料需求", "Workspace", typeof(PurchaseDemandAssociationViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Edit", AnyRequiredPermissions: [PermissionKeys.ProcurementOrderRead]),
        new(PurchaseOrderTraceViewModel.RouteKey, "采购追溯", "Detail", typeof(PurchaseOrderTraceViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Read"),

        new(PurchaseOrderProgressWorkspaceViewModel.RouteKey, "供应商反馈录入", "Workspace", typeof(PurchaseOrderProgressWorkspaceViewModel), NavigationModuleKeys.Procurement, "Procurement.Progress.View"),
        new(PurchaseOrderProgressWorkspaceViewModel.DocumentRouteKey, "供应商反馈", "Document", typeof(PurchaseOrderProgressWorkspaceViewModel), NavigationModuleKeys.Procurement, "Procurement.Progress.View"),

        new(PurchaseOrderDetailViewModel.RouteKey, "采购单详情", "Workspace", typeof(PurchaseOrderDetailViewModel), NavigationModuleKeys.Procurement, "Procurement.Order.Read"),

        new(AfterSalesManagerViewModel.RouteKey, "售后服务管理", "Manager", typeof(AfterSalesManagerViewModel), NavigationModuleKeys.QualityControl, "QualityControl.AfterSales.View"),
        new(FlowManagerViewModel.RouteKey, "流量管理", "Manager", typeof(FlowManagerViewModel), NavigationModuleKeys.QualityControl, PermissionKeys.QualityControlFlowView),
        new(FlowDetailViewModel.RouteKey, "流量测试组", "Detail", typeof(FlowDetailViewModel), NavigationModuleKeys.QualityControl, PermissionKeys.QualityControlFlowView),

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
                    new(SwiftExportClient.Features.Products.PackagingMaterials.ViewModels.PackagingMaterialManagerViewModel.RouteKey, "包材管理", "Workspace", "Box24"),
                    new("product-category-manager", "产品分类", "Workspace", "Folder24"),
                    new(ProductAttributeManagerViewModel.RouteKey, "自定义属性", "Workspace", "DocumentTable24"),
                    new("product-colors", "产品颜色", "Workspace", "Color24"),
                    new("product-bom", "BOM 管理", "Workspace", "Branch24"),
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
                    new(CustomerProductManagerViewModel.RouteKey, "客户产品", "Workspace", "DocumentTable24"),
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
                    new(SupplierProductManagerViewModel.RouteKey, "供应商产品", "Workspace", "DocumentTable24"),
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
                NavigationModuleKeys.OrderTracking,
                "订单跟踪",
                "Workspace",
                "DataTrending24",
                IsExpanded: false,
                Children: [new(OrderTrackingOverviewViewModel.RouteKey, "跟踪总览", "Workspace", "DocumentTable24"), new(PurchaseOrderTrackingViewModel.RouteKey, "采购订单跟踪", "Workspace", "ClipboardTaskListLtr24")]),
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
                    new(PurchaseOrderProgressWorkspaceViewModel.RouteKey, "供应商反馈录入", "Workspace", "ClipboardTaskListLtr24"),
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
                    new(FlowManagerViewModel.RouteKey, "流量管理", "Manager", "DocumentTable24"),
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
