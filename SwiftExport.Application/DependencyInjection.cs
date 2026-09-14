// SwiftExport.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Mappings;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Application.Services.Orders;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Procurement;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Application.Services.Suppliers;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Application.Services.SystemServices;
using SwiftExport.Application.Services.Auth;
namespace SwiftExport.Application;

/// <summary>注册 Application 服务及其请求级依赖，不创建业务会话之外的实例。</summary>
public static class DependencyInjection
{
    /// <summary>将业务接口与实现注册到容器；服务与仓储共享请求级数据库会话。</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 1. Mapster 对象映射配置（必须最先注册）
        MapsterConfig.RegisterMappings(services);

        services.AddLogging();

        services.AddScoped<ServiceBaseContext>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserPermissionService, UserPermissionService>();


        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IProductMasterService, ProductMasterService>();
        services.AddScoped<IProductNeutralManagerService, ProductNeutralManagerService>();
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
        services.AddScoped<ISalesOrderManagerService, SalesOrderManagerService>();
        services.AddScoped<ISalesOrderDetailService, SalesOrderDetailService>();
        services.AddScoped<ISalesFulfillmentManagerService, SalesFulfillmentManagerService>();
        services.AddScoped<ISalesFulfillmentDetailService, SalesFulfillmentDetailService>();
        services.AddScoped<IGrossDemandWorkspaceService, GrossDemandWorkspaceService>();
        services.AddScoped<IMaterialDemandEstablishmentService, MaterialDemandEstablishmentService>();
        services.AddScoped<ISourceDemandReceptionService, SourceDemandReceptionService>();
        services.AddScoped<ISourceDeliveryService, SourceDeliveryService>();
        services.AddScoped<ISourceDemandDeliveryService, SourceDemandDeliveryService>();
        services.AddScoped<GrossDemandDecompositionPlanner>();
        services.AddScoped<MaterialDemandSuggestedSupplierInitializer>();
        services.AddScoped<NetDemandInitialAllocationPlanner>();
        services.AddScoped<NetDemandPlanReplacementPlanner>();
        services.AddScoped<NetDemandPlanCommandIdempotencyPolicy>();
        services.AddScoped<PurchaseDraftPreviewBuilder>();
        services.AddScoped<PurchaseDraftWriteModelBuilder>();
        services.AddScoped<PurchaseDraftCreationService>();
        services.AddScoped<PurchaseDraftReleasePlanner>();
        services.AddScoped<PurchaseDraftLifecycleService>();
        services.AddScoped<IPurchaseDraftService, PurchaseDraftService>();
        // 新物料服务独立演进；旧混合接口仅兼容未迁移调用方。
        services.AddScoped<INetDemandServices, NetDemandServices>();
        services.AddScoped<INetDemandWorkspaceService, NetDemandWorkspaceService>();
        services.AddScoped<IPurchaseOrderWorkspaceService, PurchaseOrderWorkspaceService>();
        // 独立采购按查询、命令和确认依据分别注册，不复用旧 Workspace。
        services.AddScoped<SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderTransaction>();
        services.AddScoped<SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementConfirmationBuilder>();
        services.AddScoped<IProcurementConfirmationQueryService, SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementConfirmationQueryService>();
        services.AddScoped<IProcurementOrderCommandService, SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderCommandService>();
        services.AddScoped<IProcurementOrderQueryService, SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderQueryService>();
        services.AddScoped<IPurchaseOrderManagerQueryService, SwiftExport.Application.Services.Procurement.OrderManagement.PurchaseOrderManagerQueryService>();
        services.AddScoped<IBomExecutionReader, SwiftExport.Application.Services.Products.Execution.BomExecutionReader>();
        services.AddScoped<SwiftExport.Application.Interfaces.MasterData.IExecutionEligibilityService, SwiftExport.Application.Services.MasterData.ExecutionEligibilityService>();
        services.AddScoped<SwiftExport.Application.Interfaces.MasterData.IProcurementMasterDataQueryService, SwiftExport.Application.Services.MasterData.ProcurementMasterDataQueryService>();
        services.AddScoped<IInventoryPostingService, InventoryPostingService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IInboundManagerService, InboundManagerService>();
        services.AddScoped<IWarehouseCandidateQueryService, WarehouseCandidateQueryService>();
        services.AddScoped<IInboundPostingPort, InboundPostingPort>();
        services.AddScoped<IOutboundPostingPort, OutboundPostingPort>();
        services.AddScoped<IOutboundWorkspaceService, OutboundWorkspaceService>();
        services.AddScoped<PackingListPreviewBuilder>();
        services.AddScoped<IPackingListWorkspaceService, PackingListWorkspaceService>();
        services.AddScoped<IDocumentNoService, DocumentNoService>();
        services.AddScoped<ISysDicCurrencyService, SysDicCurrencyService>();
        services.AddScoped<ISysDicCurrencyExchangeService, SysDicCurrencyExchangeService>();
        // 视图路由配置服务
        services.AddScoped<ISysViewRouteService, SysViewRouteService>();

        services.AddScoped<SwiftExport.Application.Interfaces.QualityControl.IAfterSalesService, SwiftExport.Application.Services.QualityControl.AfterSalesService>();
        return services;
    }
}
