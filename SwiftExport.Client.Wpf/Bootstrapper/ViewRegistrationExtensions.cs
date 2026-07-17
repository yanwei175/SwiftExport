using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Placeholders.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Shell;

namespace SwiftExportClient.Bootstrapper;

public static class ViewRegistrationExtensions
{
    public static IServiceCollection AddSwiftExportClientViews(this IServiceCollection services)
    {
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<ShellWindow>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<BomDrawerViewModel>();
        services.AddTransient<ProductColorsViewModel>();
        services.AddTransient<ProductCategoryManagerViewModel>();
        services.AddTransient<ProductBomViewModel>();
        services.AddTransient<CustomerManagerViewModel>();
        services.AddTransient<CustomerProductManagerViewModel>();
        services.AddTransient<SupplierManagerViewModel>();
        services.AddTransient<SupplierProductManagerViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<SalesOrderManagerViewModel>();
        services.AddTransient<SalesOrderDetailViewModel>();
        services.AddTransient<GrossDemandWorkspaceViewModel>();
        services.AddTransient<GrossDemandManualEditorViewModel>();
        services.AddTransient<GrossDemandTraceViewModel>();
        services.AddTransient<NetDemandWorkspaceViewModel>();
        services.AddTransient<NetDemandPlanEditorViewModel>();
        services.AddTransient<NetDemandTraceViewModel>();
        services.AddTransient<PurchaseDraftPreviewViewModel>();
        services.AddTransient<PurchaseOrderWorkspaceViewModel>();
        services.AddTransient<PurchaseOrderDetailViewModel>();
        services.AddTransient<NetDemandPurchasePickerViewModel>();
        services.AddTransient<PlanningViewModel>();
        services.AddTransient<WarehouseSitesViewModel>();
        services.AddTransient<InventoryBalancesViewModel>();
        services.AddTransient<InventoryLedgerViewModel>();

        return services;
    }
}
