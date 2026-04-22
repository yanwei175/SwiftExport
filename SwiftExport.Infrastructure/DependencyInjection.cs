using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Suppliers;
using SwiftExport.Infrastructure.Repositories.Systems;
using SwiftExport.Infrastructure.Repositories.SystemRepositories;

namespace SwiftExport.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddLogging();

        services.AddScoped<DbSession>();
        services.AddScoped<RepositoryBaseContext>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();


        services.AddScoped(typeof(IRepository<>), typeof(DapperRepository<>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IBomRepository, BomRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerPoConfigRepository, CustomerPoConfigRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IProductColorRepository, ProductColorRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<ISysDicCurrencyRepository, SysDicCurrencyRepository>();
        services.AddScoped<ISysDicCurrencyExchangeRepository, SysDicCurrencyExchangeRepository>();
        services.AddScoped<ISysDictDataRepository, SysDictDataRepository>();
        // 视图路由配置仓储
        services.AddScoped<ISysViewRouteRepository, SysViewRouteRepository>();

        return services;
    }
}