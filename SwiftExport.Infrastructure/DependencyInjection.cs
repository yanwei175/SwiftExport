using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers;
using SwiftExport.Infrastructure.Repositories.DemandPlanning;
using SwiftExport.Infrastructure.Repositories.MasterData;
using SwiftExport.Infrastructure.Repositories.Orders;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Suppliers;
using SwiftExport.Infrastructure.Repositories.SystemRepositories;
using SwiftExport.Infrastructure.Repositories.Systems;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddLogging();

        services.AddScoped<DbSession>();
        services.AddScoped<RepositoryBaseContext>();

        // ══════════════════════════════════════════════════════
        // 认证相关服务
        // ══════════════════════════════════════════════════════
        // AuditStampService 依赖 ICurrentUser，ICurrentUser 在 WebApi 层注册（读 HttpContext）
        // Infrastructure 层只依赖接口，不依赖具体实现，保持分层干净
        services.AddScoped<IAuditStampService, AuditStampService>();
        services.AddScoped<ISysUserRepository, SysUserRepository>();
        // 角色主表仓储
        services.AddScoped<ISysRoleRepository, SysRoleRepository>();
        // 用户-角色关联仓储
        services.AddScoped<ISysUserRoleRepository, SysUserRoleRepository>();


        services.AddScoped(typeof(IRepository<>), typeof(DapperRepository<>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IBomRepository, BomRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerPoConfigRepository, CustomerPoConfigRepository>();
        // 注册客户产品仓储
        services.AddScoped<ICustomerProductRepository, CustomerProductRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<ISupplierProductRepository, SupplierProductRepository>();
        services.AddScoped<IProductColorRepository, ProductColorRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<ISysDicCurrencyRepository, SysDicCurrencyRepository>();
        services.AddScoped<ISysDicCurrencyExchangeRepository, SysDicCurrencyExchangeRepository>();
        services.AddScoped<IDocumentSequenceRepository, DocumentSequenceRepository>();
        services.AddScoped<ISysDictDataRepository, SysDictDataRepository>();
        // 视图路由配置仓储
        services.AddScoped<ISysViewRouteRepository, SysViewRouteRepository>();
        services.AddScoped<ISalesOrderLineRepository, SalesOrderLineRepository>();
        services.AddScoped<IDemandExecutionLinkRepository, DemandExecutionLinkRepository>();
        services.AddScoped<IGrossDemandRepository, GrossDemandRepository>();
        services.AddScoped<INetDemandRepository, NetDemandRepository>();
        services.AddScoped<ISalesOrderHeadRepository, SalesOrderHeadRepository>();
        services.AddScoped<IPurchaseOrderHeadRepository, PurchaseOrderRepository>();
        services.AddScoped<IPurchaseOrderLineRepository, PurchaseOrderLineRepository>();
        services.AddScoped<IReceiveHeadRepository, ReceiveHeadRepository>();
        services.AddScoped<IReceiveLineRepository, ReceiveLineRepository>();
        services.AddScoped<IOutboundHeadRepository, OutboundHeadRepository>();
        services.AddScoped<IOutboundLineRepository, OutboundLineRepository>();
        services.AddScoped<IPackingListHeadRepository, PackingListHeadRepository>();
        services.AddScoped<IPackingListLineRepository, PackingListLineRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();

        return services;
    }
}
