using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;
using SwiftExportClient.Features.DemandPlanning.NetDemand.ViewModels;
using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Placeholders.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Fulfillment.ViewModels;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Suppliers.Components.ViewModel;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
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
        services.AddTransient<SupplierPickerViewModel>();
        services.AddTransient<Func<SupplierPickerViewModel>>(provider =>
            () => provider.GetRequiredService<SupplierPickerViewModel>());


        services.AddTransient<SalesViewModel>();
        services.AddTransient<SalesOrderManagerViewModel>();
        services.AddTransient<SalesOrderDetailViewModel>();
        services.AddTransient<SalesFulfillmentManagerViewModel>();
        services.AddTransient<SalesFulfillmentDetailViewModel>();
        services.AddTransient<GrossDemandWorkspaceViewModel>();
        services.AddTransient<GrossDemandManualEditorViewModel>();
        services.AddTransient<GrossDemandTraceViewModel>();
        services.AddTransient<NetDemandManagerViewModel>();
        services.AddTransient<SourceDemandCandidatePickerViewModel>();
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
        services.AddTransient<Func<WarehousePickerViewModel>>(provider =>
            () => ActivatorUtilities.CreateInstance<WarehousePickerViewModel>(provider));

        services.AddTransient<InventoryBalancesViewModel>();
        services.AddTransient<InventoryLedgerViewModel>();
        services.AddTransient<InboundManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.Shipping.PackingLists.ViewModels.PackingListManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.Shipping.PackingLists.ViewModels.PackingListDetailViewModel>();
        services.AddTransient<SwiftExportClient.Features.Shipping.PackingLists.Services.IPackingListSourcePickerService, SwiftExportClient.Features.Shipping.PackingLists.Services.PackingListSourcePickerService>();
        services.AddTransient<SwiftExportClient.Features.Shipping.PackingLists.Services.IPackingListPostingDialogService, SwiftExportClient.Features.Shipping.PackingLists.Services.PackingListPostingDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.ViewModels.OutboundManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.Services.IOutboundPostDialogService, SwiftExportClient.Features.Inventory.Outbounds.Services.OutboundPostDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.Services.IOutboundReverseDialogService, SwiftExportClient.Features.Inventory.Outbounds.Services.OutboundReverseDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.Services.IOutboundIssueReasonDialogService, SwiftExportClient.Features.Inventory.Outbounds.Services.OutboundIssueReasonDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.Services.IOutboundPersistenceService, SwiftExportClient.Features.Inventory.Outbounds.Services.OutboundPersistenceService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.Services.IOutboundDeleteDialogService, SwiftExportClient.Features.Inventory.Outbounds.Services.OutboundDeleteDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Outbounds.Services.IOutboundItemDialogService, SwiftExportClient.Features.Inventory.Outbounds.Services.OutboundItemDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Inbounds.Services.IInboundReverseDialogService, SwiftExportClient.Features.Inventory.Inbounds.Services.InboundReverseDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Inbounds.Services.IInboundPostDialogService, SwiftExportClient.Features.Inventory.Inbounds.Services.InboundPostDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Inbounds.Services.IInboundPersistenceService, SwiftExportClient.Features.Inventory.Inbounds.Services.InboundPersistenceService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Inbounds.Services.IInboundDeleteDialogService, SwiftExportClient.Features.Inventory.Inbounds.Services.InboundDeleteDialogService>();
        services.AddTransient<SwiftExportClient.Features.Inventory.Inbounds.Services.IInboundSourceDialogService, SwiftExportClient.Features.Inventory.Inbounds.Services.InboundSourceDialogService>();



        services.AddTransient<SettingsCenterViewModel>();

        services.AddTransient<ChangePasswordViewModel>();
        services.AddTransient<ChangePasswordWindow>();
        services.AddTransient<UserRegistrationViewModel>();
        services.AddTransient<ResetPasswordViewModel>();

        return services;
    }
}
