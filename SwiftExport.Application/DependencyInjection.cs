// SwiftExport.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Mappings;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.DemandPlanning;
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
        // 1. Mapster 对象映射配置（必须最先注册）
        MapsterConfig.RegisterMappings(services);

        services.AddLogging();

        services.AddScoped<ServiceBaseContext>();


        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IBomService, BomService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerPoConfigService, CustomerPoConfigService>();
        // ✅ 补充注册客户产品服务
        services.AddScoped<ICustomerProductService, CustomerProductServices>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<ISupplierProductService, SupplierProductService>();
        services.AddScoped<IProductColorService, ProductColorService>();
        services.AddScoped<IProductCategoryService, ProductCategoryService>();
        services.AddScoped<ISysDictDataService, SysDictDataService>();
        services.AddScoped<ISysDictTypeService, SysDictTypeService>();
        services.AddScoped<ISalesOrderWorkspaceService, SalesOrderWorkspaceService>();
        services.AddScoped<IGrossDemandWorkspaceService, GrossDemandWorkspaceService>();
        services.AddScoped<GrossDemandDecompositionPlanner>();
        services.AddScoped<IIndependentDemandServices, IndependentDemandService>();
        services.AddScoped<IDependentDemandService, DependentDemandService>();
        services.AddScoped<IDocumentNoService, DocumentNoService>();
        services.AddScoped<ISysDicCurrencyService, SysDicCurrencyService>();
        services.AddScoped<ISysDicCurrencyExchangeService, SysDicCurrencyExchangeService>();
        // 视图路由配置服务
        services.AddScoped<ISysViewRouteService, SysViewRouteService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IFollowUpService, FollowUpService>();
        services.AddScoped<IReceiptService, ReceiveService>();
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IPackingListService, PackingListService>();

        return services;
    }
}
