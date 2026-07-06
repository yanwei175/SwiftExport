using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Home;
using SwiftExportClient.Features.Planning;
using SwiftExportClient.Features.Products.Categories.Services;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Placeholders.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Purchasing;
using SwiftExportClient.Features.Sales;
using SwiftExportClient.Features.Shipping;
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
        services.AddTransient<ProductColorsViewModel>();
        services.AddTransient<ProductCategoryManagerViewModel>();
        services.AddTransient<IProductCategoryManagerDialogService, ProductCategoryManagerDialogService>();
        services.AddTransient<ProductBomViewModel>();
        services.AddTransient<CustomerProductsViewModel>();
        services.AddTransient<SupplierProductsViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<PlanningViewModel>();
        services.AddTransient<PurchasingViewModel>();
        services.AddTransient<ShippingViewModel>();

        return services;
    }
}
