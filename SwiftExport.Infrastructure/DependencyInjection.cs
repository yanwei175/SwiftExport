using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers;
using SwiftExport.Infrastructure.Repositories.DemandPlanning;
using SwiftExport.Infrastructure.Repositories.Orders;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Procurement;
using SwiftExport.Infrastructure.Repositories.Suppliers;
using SwiftExport.Infrastructure.Repositories.SystemRepositories;
using SwiftExport.Infrastructure.Repositories.Systems;
using SwiftExport.Infrastructure.Repositories.Inventory;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Infrastructure;

/// <summary>注册数据会话、仓储与基础设施服务，保持请求级依赖共享。</summary>
public static class DependencyInjection
{
    /// <summary>注册 Infrastructure 实现；当前用户由宿主提供，不在这里创建连接配置。</summary>
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
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IUserPermissionRepository, UserPermissionRepository>();
        // 角色主表仓储
        services.AddScoped<ISysRoleRepository, SysRoleRepository>();
        // 用户-角色关联仓储
        services.AddScoped<ISysUserRoleRepository, SysUserRoleRepository>();


        services.AddScoped(typeof(IRepository<>), typeof(DapperRepository<>));
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductMasterRepository, ProductMasterRepository>();
        services.AddScoped<IProductNeutralRepository, ProductNeutralRepository>();
        services.AddScoped<IProductNeutralManagerRepository, ProductNeutralManagerRepository>();
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
        services.AddScoped<ISysDictTypeRepository, SysDictTypeRepository>();
        // 视图路由配置仓储
        services.AddScoped<ISysViewRouteRepository, SysViewRouteRepository>();
        services.AddScoped<ISalesOrderLineRepository, SalesOrderLineRepository>();
        services.AddScoped<INetDemandExecutionLinkRepository, NetDemandExecutionLinkRepository>();
        services.AddScoped<IGrossDemandRepository, GrossDemandRepository>();
        services.AddScoped<INetDemandRepository, NetDemandRepository>();
        services.AddScoped<INetDemandAllocationRepository, NetDemandAllocationRepository>();
        services.AddScoped<INetDemandPlanCommandRepository, NetDemandPlanCommandRepository>();
        services.AddScoped<IPurchaseOrderDraftRepository, PurchaseOrderDraftRepository>();
        services.AddScoped<IPurchaseOrderWorkspaceRepository, PurchaseOrderWorkspaceRepository>();
        // 新链的事务持久化、管理投影与严格 BOM 读取使用同一请求 DbSession。
        services.AddScoped<IProcurementOrderRepository, ProcurementOrderRepository>();
        services.AddScoped<IProcurementOrderQueryRepository, ProcurementOrderQueryRepository>();
        services.AddScoped<IPurchaseOrderManagerQueryRepository, PurchaseOrderManagerQueryRepository>();
        services.AddScoped<IProcurementConfirmationRepository, ProcurementConfirmationRepository>();
        services.AddScoped<IBomExecutionRepository, BomExecutionRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.MasterData.IExecutionEligibilityRepository, SwiftExport.Infrastructure.Repositories.MasterData.ExecutionEligibilityRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.MasterData.IProcurementMasterDataQueryRepository, SwiftExport.Infrastructure.Repositories.MasterData.ProcurementMasterDataQueryRepository>();
        services.AddScoped<ISupplierProgressReportRepository, SupplierProgressReportRepository>();
        services.AddScoped<IBusinessOperationLogRepository, BusinessOperationLogRepository>();
        services.AddScoped<ISalesOrderHeadRepository, SalesOrderHeadRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Orders.ISalesFulfillmentManagerRepository, SalesFulfillmentManagerRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Orders.ISalesFulfillmentDetailRepository, SalesFulfillmentDetailRepository>();
        services.AddScoped<IWarehouseSiteRepository, WarehouseSiteRepository>();
        services.AddScoped<IWarehouseCandidateRepository, WarehouseCandidateRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IOutboundDocumentRepository, OutboundDocumentRepository>();
        services.AddScoped<IPackingListDocumentRepository, PackingListDocumentRepository>();
        services.AddScoped<PackingListWorkspaceRepository>();
        services.AddScoped<IPackingListPlanningRepository>(provider => provider.GetRequiredService<PackingListWorkspaceRepository>());
        services.AddScoped<IPackingListQueryRepository>(provider => provider.GetRequiredService<PackingListWorkspaceRepository>());
        services.AddScoped<ISourceDeliveryRepository, SourceDeliveryRepository>();

        services.AddScoped<SwiftExport.Core.Interfaces.QualityControl.IAfterSalesRepository, SwiftExport.Infrastructure.Repositories.QualityControl.AfterSalesRepository>();
        return services;
    }
}
