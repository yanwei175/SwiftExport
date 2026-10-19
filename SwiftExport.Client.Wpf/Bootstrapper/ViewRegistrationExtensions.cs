using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Permissions.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Profile.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.Services;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Services.UI;
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
using SwiftExportClient.Features.Products.Bom.Services;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.QualityControl.AfterSales.ViewModels;
using SwiftExportClient.Features.QualityControl.Categories.ViewModels;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Features.Suppliers.Components.ViewModel;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.Views;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Shell;

namespace SwiftExportClient.Bootstrapper;

/// <summary>注册客户端窗口、视图模型及其瞬态生命周期。</summary>
public static class ViewRegistrationExtensions
{
    /// <summary>注册 Shell 及各业务页签的瞬态视图模型，由导航作用域负责释放。</summary>
    public static IServiceCollection AddSwiftExportClientViews(this IServiceCollection services)
    {
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<ShellWindow>();
        services.AddTransient<HomeViewModel>();

        services.AddTransient<ProductsViewModel>();
        services.AddTransient<SwiftExportClient.Features.Products.PackagingMaterials.ViewModels.PackagingMaterialManagerViewModel>();
        services.AddTransient<ProductMasterManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.Products.ProductMasterImport.Services.ProductMasterWorkbookService>();
        services.AddTransient<SwiftExportClient.Features.Products.ProductMasterImport.ViewModels.ProductMasterImportViewModel>();
        services.AddTransient<ProductNeutralManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.Products.ProductNeutralImport.Services.ProductNeutralWorkbookService>();
        services.AddTransient<SwiftExportClient.Features.Products.ProductNeutralImport.ViewModels.ProductNeutralImportViewModel>();
        services.AddTransient<ProductMasterDetailViewModel>();
        services.AddTransient<ProductDetailViewModel>();

        services.AddTransient<BomDrawerViewModel>();

        services.AddTransient<ProductColorsViewModel>();

        services.AddTransient<ProductCategoryAssignmentViewModel>();
        services.AddTransient<ProductCategoryManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.Products.Attributes.ViewModels.ProductAttributeManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels.DictionaryDataEditorViewModel>();
        services.AddTransient<SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels.DictionaryDataManagerViewModel>();

        services.AddTransient<BomManagerViewModel>();
        services.AddTransient<IBomBindingClientService, BomBindingClientService>();
        services.AddTransient<IBomManagementDialogService, BomManagementDialogService>();

        services.AddTransient<CustomerManagerViewModel>();
        services.AddTransient<CustomerPoConfigManagerViewModel>();
        services.AddTransient<CustomerProductManagerViewModel>();

        services.AddTransient<SupplierManagerViewModel>();
        services.AddTransient<SupplierProductManagerViewModel>();
        services.AddTransient<SupplierPickerViewModel>();
        services.AddTransient<Func<SupplierPickerViewModel>>(provider =>
            () => provider.GetRequiredService<SupplierPickerViewModel>());


        services.AddTransient<SalesViewModel>();
        services.AddTransient<SalesOrderManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.OrderTracking.Overview.ViewModels.OrderTrackingOverviewViewModel>();
        services.AddTransient<SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels.DemandTrackingDetailViewModel>();
        services.AddTransient<SwiftExportClient.Features.OrderTracking.MaterialDetail.ViewModels.MaterialTrackingDetailViewModel>();
        services.AddTransient<SalesOrderDetailViewModel>();
        services.AddTransient<GrossDemandWorkspaceViewModel>();
        services.AddTransient<GrossDemandManualEditorViewModel>();
        services.AddTransient<GrossDemandTraceViewModel>();
        services.AddTransient<NetDemandManagerViewModel>();
        services.AddTransient<MaterialDemandTraceViewModel>();
        services.AddTransient<SourceDemandCandidatePickerViewModel>();
        services.AddTransient<PurchaseDraftWorkflowViewModel>();
        services.AddTransient<PurchasePreparationService>();
        services.AddTransient<PurchaseOrderManagerViewModel>();
        services.AddTransient<PurchaseOrderProgressWorkspaceViewModel>();
        services.AddTransient<PurchaseOrderTraceViewModel>();
        services.AddTransient<PurchaseOrderDetailViewModel>();
        services.AddTransient<PurchaseDemandAssociationViewModel>();
        services.AddTransient<AfterSalesManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels.FlowManagerViewModel>();
        services.AddTransient<SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels.FlowDetailViewModel>();
        services.AddTransient<AfterSalesDetailViewModel>();
        services.AddTransient<AfterSalesQualityCategoryManagerViewModel>();
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



        // 每个设置页签创建独立 Dialog 用例服务；列表与表单共用当前页签的活动门禁。
        services.AddTransient<SettingsPageViewModel>(provider =>
        {
            var dialogs = new SettingsDialogService(
                provider.GetRequiredService<IDialogService>(), provider.GetRequiredService<ICurrentUserService>(),
                () => ActivatorUtilities.CreateInstance<ChangePasswordViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<UserRegistrationViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<ResetPasswordViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<UserProfileFormViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<UserRolesFormViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<RoleProfileFormViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<RolePermissionsFormViewModel>(provider));
            return new SettingsPageViewModel(
                () => ActivatorUtilities.CreateInstance<UsersQueryViewModel>(provider, dialogs),
                () => ActivatorUtilities.CreateInstance<RolesQueryViewModel>(provider, dialogs),
                () => ActivatorUtilities.CreateInstance<PermissionsQueryViewModel>(provider),
                () => ActivatorUtilities.CreateInstance<DictionaryManagerViewModel>(provider),
                provider.GetRequiredService<ICurrentUserService>(), dialogs,
                provider.GetRequiredService<IAuthService>(), provider.GetRequiredService<IMessageService>(),
                () => ActivatorUtilities.CreateInstance<SwiftExportClient.Features.SystemSettings.SystemParameters.ViewModels.SystemParametersViewModel>(provider),
                provider.GetRequiredService<SwiftExportClient.Services.Caching.ISystemParameterCacheService>());
        });
        services.AddTransient<ChangePasswordViewModel>();
        services.AddTransient<UserRegistrationViewModel>();
        services.AddTransient<ResetPasswordViewModel>();

        return services;
    }
}
