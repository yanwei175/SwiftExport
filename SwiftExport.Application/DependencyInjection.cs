// SwiftExport.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Mappings;
using SwiftExport.Application.Services.Products;
namespace SwiftExport.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 1. 注册 AutoMapper
        // 自动扫描当前程序集里的所有 Profile
        services.AddAutoMapper(cfg => { }, typeof(ProductProfile).Assembly);

        // 2. 注册 Services
        // 你可以在这里写死，也可以在这里用 Scrutor 扫描，反正只影响这一层
        services.AddScoped<IProductService, ProductService>();

        // 以后加了 OrderService，就加在这里，不用动 Program.cs
        // services.AddScoped<IOrderService, OrderService>();

        return services;
    }
}