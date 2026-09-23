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
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Services.Inventory.Validation.Inbounds;
using SwiftExport.Application.Services.Inventory.Validation.Outbounds;
using FluentValidation;
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
        services.AddScoped<BusinessCommandExecutor>();
        services.AddScoped<CommandRequestValidator>();
        services.AddScoped<CommandPipeline>();
        services.AddScoped<IValidator<SwiftExport.Contracts.DTOs.Inventory.Inbounds.CreateInboundDraftCommand>, CreateInboundDraftCommandValidator>();
        services.AddScoped<IValidator<SwiftExport.Contracts.DTOs.Inventory.Inbounds.UpdateInboundDraftCommand>, UpdateInboundDraftCommandValidator>();
        services.AddScoped<IValidator<SwiftExport.Contracts.DTOs.Inventory.Inbounds.DeleteInboundDraftCommand>, DeleteInboundDraftCommandValidator>();
        services.AddScoped<IValidator<SwiftExport.Contracts.DTOs.Inventory.Outbounds.CreateOutboundDraftCommand>, CreateOutboundDraftCommandValidator>();
        services.AddScoped<IValidator<SwiftExport.Contracts.DTOs.Inventory.Outbounds.UpdateOutboundDraftCommand>, UpdateOutboundDraftCommandValidator>();
        services.AddScoped<IValidator<SwiftExport.Contracts.DTOs.Inventory.Outbounds.DeleteOutboundDraftCommand>, DeleteOutboundDraftCommandValidator>();
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
        services.AddScoped<IGrossDemandWorkspaceService, GrossDemandWorkspaceService>();
        services.AddScoped<IMaterialDemandEstablishmentService, MaterialDemandEstablishmentService>();
        services.AddScoped<ISourceDemandReceptionService, SourceDemandReceptionService>();
        services.AddScoped<ISourceDeliveryService, SourceDeliveryService>();
        services.AddScoped<ISourceDemandDeliveryService, SourceDemandDeliveryService>();
        services.AddScoped<IPackingListService, PackingListService>();
        services.AddScoped<IPackingListTraceService, PackingListTraceService>();
        services.AddScoped<IPackingListPostingCoordinator, PackingListPostingCoordinator>();
        services.AddScoped<IPackingListPostingService, PackingListPostingService>();
        services.AddScoped<GrossDemandDecompositionPlanner>();
        services.AddScoped<MaterialDemandSuggestedSupplierInitializer>();
        services.AddScoped<PurchaseDraftPreviewBuilder>();
        services.AddScoped<PurchaseDraftWriteModelBuilder>();
        services.AddScoped<PurchaseDraftCreationService>();
        services.AddScoped<IPurchaseDraftService, PurchaseDraftService>();
        // 物料事实统一进入 Manager，旧计划编辑服务不再注册。
        services.AddScoped<INetDemandManagerService, NetDemandManagerService>();
        // 独立采购按查询、命令和确认依据分别注册，不复用旧 Workspace。
        services.AddScoped<SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderTransaction>();
        services.AddScoped<SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementConfirmationBuilder>();
        services.AddScoped<IProcurementOrderCommandService, SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderCommandService>();
        services.AddScoped<IProcurementOrderReversalService, SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderReversalService>();
        services.AddScoped<IProcurementOrderQueryService, SwiftExport.Application.Services.Procurement.OrderManagement.ProcurementOrderQueryService>();
        services.AddScoped<IPurchaseOrderManagerQueryService, SwiftExport.Application.Services.Procurement.OrderManagement.PurchaseOrderManagerQueryService>();
        services.AddScoped<IPurchaseOrderProgressService, SwiftExport.Application.Services.Procurement.OrderManagement.PurchaseOrderProgressService>();
        services.AddScoped<IBomExecutionReader, SwiftExport.Application.Services.Products.Execution.BomExecutionReader>();
        services.AddScoped<SwiftExport.Application.Interfaces.MasterData.IExecutionEligibilityService, SwiftExport.Application.Services.MasterData.ExecutionEligibilityService>();
        services.AddScoped<SwiftExport.Application.Interfaces.MasterData.IProcurementMasterDataQueryService, SwiftExport.Application.Services.MasterData.ProcurementMasterDataQueryService>();
        services.AddScoped<IInventoryPostingService, InventoryPostingService>();
        services.AddScoped<AutomaticPostingScope>();
        services.AddScoped<IAutomaticPostingScope>(sp => sp.GetRequiredService<AutomaticPostingScope>());
        services.AddScoped<AutomaticPostingPorts>();
        services.AddScoped<IInboundPostingPort>(sp => sp.GetRequiredService<AutomaticPostingPorts>());
        services.AddScoped<IOutboundPostingPort>(sp => sp.GetRequiredService<AutomaticPostingPorts>());
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IInboundManagerService, InboundManagerService>();
        services.AddScoped<IOutboundManagerService, OutboundManagerService>();
        services.AddScoped<IWarehouseCandidateQueryService, WarehouseCandidateQueryService>();
        services.AddScoped<IDocumentNoService, DocumentNoService>();
        services.AddScoped<ISysDicCurrencyService, SysDicCurrencyService>();
        services.AddScoped<ISysDicCurrencyExchangeService, SysDicCurrencyExchangeService>();
        // 视图路由配置服务
        services.AddScoped<ISysViewRouteService, SysViewRouteService>();

        services.AddScoped<SwiftExport.Application.Interfaces.QualityControl.IAfterSalesService, SwiftExport.Application.Services.QualityControl.AfterSalesService>();
        return services;
    }
}
