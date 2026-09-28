using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.QualityControl.AfterSales.Services;
using SwiftExportClient.Features.Sales.Orders.Services;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Suppliers.Components.Services;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.Images;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Updates;
using Wpf.Ui;

namespace SwiftExportClient.Bootstrapper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwiftExportClientServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.Configure<ClientOptions>(configuration.GetSection(ClientOptions.SectionName));
        services.Configure<ProductOptions>(configuration.GetSection(ProductOptions.SectionName));
        services.Configure<ImageCacheOptions>(configuration.GetSection(ImageCacheOptions.SectionName));
        services.AddHttpClient(ImageCacheService.HttpClientName, client =>
            client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false,
                UseDefaultCredentials = false,
            });
        services.AddSingleton<ITokenStore, LocalTokenStore>();
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<INavigationRouteRegistry, NavigationRouteRegistry>();
        services.AddSingleton<INavigationAccessService, NavigationAccessService>();
        services.AddSingleton<INavigationTabFactory, NavigationTabFactory>();
        services.AddSingleton<Navigation.INavigationService, Navigation.NavigationService>();
        services.AddSingleton<IApiCallRunner, ApiCallRunner>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<IStatusMessageService, StatusMessageService>();
        services.AddSingleton<IMessageService, WpfUiMessageService>();
        services.AddSingleton<IDialogService, WpfUiDialogService>();
        services.AddSingleton<IClientVersionService, ClientVersionService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<IReferenceDataCacheService, ReferenceDataCacheService>();
        services.AddTransient<INetDemandDialogService, NetDemandDialogService>();
        services.AddTransient<IProcurementDialogService, ProcurementDialogService>();
        services.AddTransient<IProcurementReversalDialogService, ProcurementReversalDialogService>();
        services.AddSingleton<ProcurementReversalSession>();
        services.AddTransient<IAfterSalesFeatureService, AfterSalesFeatureService>();
        services.AddTransient<IAfterSalesReasonDialogService, AfterSalesReasonDialogService>();
        services.AddTransient<IWarehouseSiteReasonDialogService, WarehouseSiteReasonDialogService>();
        services.AddTransient<IWarehousePickerService, WarehousePickerService>();
        services.AddTransient<ISupplierPickerService, SupplierPickerService>();
        services.AddTransient<ISalesOrderCancellationDialogService, SalesOrderCancellationDialogService>();
        services.AddTransient<IProductMasterSyncDialogService, ProductMasterSyncDialogService>();
        services.AddTransient<IProductImageUploadClient, ProductImageUploadClient>();
        services.AddTransient<IProductImageUploadDialogService, ProductImageUploadDialogService>();
        services.AddTransient<SwiftExportClient.Features.Products.ProductList.ViewModels.ProductImageUploadViewModel>();
        services.AddTransient<AuthTokenHandler>();
        services.AddSwiftExportApis();

        return services;
    }
}
