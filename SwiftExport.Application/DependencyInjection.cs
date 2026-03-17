// SwiftExport.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Suppliers;
namespace SwiftExport.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {

        //注册 Services
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerPoConfigService, CustomerPoConfigService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IProductColorService, ProductColorService>();


        // 以后加了 OrderService，就加在这里，不用动 Program.cs
        // services.AddScoped<IOrderService, OrderService>();

        return services;
    }
}