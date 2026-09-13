using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
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
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Sales.Fulfillment.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Views;
using SwiftExportClient.Shell;

namespace SwiftExportClient.Bootstrapper;

/// <summary>注册客户端窗口、视图模型及其瞬态生命周期。</summary>
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
        services.AddTransient<ProductMasterManagerViewModel>();
        services.AddTransient<ProductNeutralManagerViewModel>();
        services.AddTransient<ProductMasterDetailViewModel>();
        services.AddTransient<BomDrawerViewModel>();
        services.AddTransient<ProductColorsViewModel>();
        services.AddTransient<ProductCategoryAssignmentViewModel>();
        services.AddTransient<ProductCategoryManagerViewModel>();
        services.AddTransient<ProductBomViewModel>();
        services.AddTransient<CustomerManagerViewModel>();
        services.AddTransient<CustomerPoConfigManagerViewModel>();
        services.AddTransient<CustomerProductManagerViewModel>();
        services.AddTransient<CustomerProductWorkspaceViewModel>();
        services.AddTransient<SupplierManagerViewModel>();
        services.AddTransient<SupplierProductManagerViewModel>();
        services.AddTransient<SupplierProductWorkspaceViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<SalesOrderManagerViewModel>();
        services.AddTransient<SalesOrderDetailViewModel>();
        services.AddTransient<SalesFulfillmentManagerViewModel>();
        services.AddTransient<SalesFulfillmentDetailViewModel>();
        services.AddTransient<GrossDemandWorkspaceViewModel>();
        services.AddTransient<GrossDemandManualEditorViewModel>();
        services.AddTransient<GrossDemandTraceViewModel>();
        services.AddTransient<NetDemandWorkspaceViewModel>();
        services.AddTransient<SourceDemandCandidatePickerViewModel>();
        services.AddTransient<NetDemandPlanEditorViewModel>();
        services.AddTransient<NetDemandTraceViewModel>();
        services.AddTransient<PurchaseDraftWorkflowViewModel>();
        services.AddTransient<PurchasePreparationService>();
        services.AddTransient<PurchaseOrderManagerViewModel>();
        services.AddTransient<PurchaseOrderProgressWorkspaceViewModel>();
        services.AddTransient<PurchaseOrderDetailViewModel>();
        services.AddTransient<AfterSalesManagerViewModel>();
        services.AddTransient<AfterSalesDetailViewModel>();
        services.AddTransient<PurchaseCandidateSelectionViewModel>();
        services.AddTransient<PurchaseOrderFromMaterialViewModel>();
        services.AddTransient<PlanningViewModel>();
        services.AddTransient<WarehouseSitesViewModel>();
        services.AddTransient<WarehousePickerViewModel>();
        services.AddTransient<InventoryBalancesViewModel>();
        services.AddTransient<InventoryLedgerViewModel>();
        services.AddTransient<InboundWorkspaceViewModel>();
        services.AddTransient<OutboundWorkspaceViewModel>();
        services.AddTransient<PackingListWorkspaceViewModel>();
        services.AddTransient<PackingListDetailViewModel>();
        services.AddTransient<SettingsCenterViewModel>();
        services.AddTransient<ChangePasswordViewModel>();
        services.AddTransient<ChangePasswordWindow>();
        services.AddTransient<UserRegistrationViewModel>();
        services.AddTransient<UserRegistrationWindow>();
        services.AddTransient<ResetPasswordViewModel>();
        services.AddTransient<ResetPasswordWindow>();

        return services;
    }
}
