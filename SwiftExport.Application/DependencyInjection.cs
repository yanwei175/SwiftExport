// SwiftExport.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.Orders;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Suppliers;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Application.Services.SystemServices;
namespace SwiftExport.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddLogging();

        services.AddScoped<ServiceBaseContext>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IBomService, BomService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerPoConfigService, CustomerPoConfigService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IProductColorService, ProductColorService>();
        services.AddScoped<IProductCategoryService, ProductCategoryService>();
        services.AddScoped<ISysDictDataService, SysDictDataService>();
        services.AddScoped<ISysDictTypeService, SysDictTypeService>();
        services.AddScoped<ISalesOrderService, SalesOrderService>();
        services.AddScoped<ISysDicCurrencyService, SysDicCurrencyService>();
        services.AddScoped<ISysDicCurrencyExchangeService, SysDicCurrencyExchangeService>();
        // 视图路由配置服务
        services.AddScoped<ISysViewRouteService, SysViewRouteService>();


        // 以后加了 OrderService，就加在这里，不用动 Program.cs
        // services.AddScoped<IOrderService, OrderService>();

        return services;
    }
}