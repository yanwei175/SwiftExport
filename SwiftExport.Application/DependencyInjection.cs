using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Delivery;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Manual;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Services.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Services.DemandPlanning.Sources.Delivery;
using SwiftExport.Application.Services.DemandPlanning.Sources.Query;
using SwiftExport.Application.Services.DemandPlanning.Sources.Manual;
using SwiftExport.Application.Services.DemandPlanning.Materials.SourceExit;
using SwiftExport.Application.Services.Inventory.AutomaticDocuments;
using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Interfaces.Inventory.Posting;
// SwiftExport.Application/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Application.Interfaces.Inventory.Query;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Mappings;
using SwiftExport.Application.Services.Customers;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
using SwiftExport.Application.Services.DemandPlanning.Materials.Sources;
using SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;
using SwiftExport.Application.Services.DemandPlanning.Materials.Reversal;
using SwiftExport.Application.Services.Orders;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Procurement;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Services.Inventory.Inbounds;
using SwiftExport.Application.Services.Inventory.Outbounds;
using SwiftExport.Application.Services.Inventory.Warehouses;
using SwiftExport.Application.Services.Inventory.Query;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Application.Services.Suppliers;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Application.Services.SystemServices;
using SwiftExport.Application.Services.Auth;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Services.Inventory.Inbounds.Validation;
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
        // 仅扫描 Application 程序集；命令仍须显式接入 Pipeline 才会执行验证。
        services.AddValidatorsFromAssemblyContaining<CreateInboundDraftCommandValidator>();
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
        services.AddScoped<IGrossDemandQueryService, GrossDemandQueryService>();
        services.AddScoped<IManualGrossDemandService, ManualGrossDemandService>();
        services.AddScoped<IMaterialDemandSourceExitService, MaterialDemandSourceExitService>();
        services.AddScoped<IMaterialDemandQueryService, MaterialDemandQueryService>();
        services.AddScoped<IMaterialDemandSourceQueryService, MaterialDemandSourceQueryService>();
        services.AddScoped<IMaterialDemandSupplierSuggestionService, MaterialDemandSupplierSuggestionService>();
        services.AddScoped<IMaterialDemandEstablishmentService, MaterialDemandEstablishmentService>();
        services.AddScoped<IMaterialDemandReversalService, MaterialDemandReversalService>();
        services.AddScoped<ISourceDemandReceptionService, SourceDemandReceptionService>();
        services.AddScoped<ISourceDeliveryService, SourceDeliveryService>();
        services.AddScoped<ISourceDemandDeliveryService, SourceDemandDeliveryService>();
        services.AddScoped<SwiftExport.Application.Interfaces.Shipping.PackingLists.IPackingListQueryService, SwiftExport.Application.Services.Shipping.PackingLists.PackingListQueryService>();
        services.AddScoped<SwiftExport.Application.Interfaces.Shipping.PackingLists.IPackingListDraftService, SwiftExport.Application.Services.Shipping.PackingLists.PackingListDraftService>();
        services.AddScoped<IPackingListTraceService, PackingListTraceService>();
        services.AddScoped<IPackingListPostingService, SwiftExport.Application.Services.Shipping.PackingLists.PackingListPostingService>();
        services.AddScoped<SwiftExport.Application.Interfaces.Shipping.PackingLists.IPackingListReversalService,
            SwiftExport.Application.Services.Shipping.PackingLists.PackingListReversalService>();
        services.AddScoped<GrossDemandDecompositionPlanner>();
        services.AddScoped<MaterialDemandSuggestedSupplierInitializer>();
        services.AddScoped<PurchaseDraftPreviewBuilder>();
        services.AddScoped<PurchaseDraftWriteModelBuilder>();
        services.AddScoped<SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand.PurchaseDraftPreviewService>();
        services.AddScoped<IPurchaseDraftPreviewService>(sp => sp.GetRequiredService<SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand.PurchaseDraftPreviewService>());
        services.AddScoped<IPurchaseDraftCreationService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand.PurchaseDraftCreationService>();
        services.AddScoped<IPurchaseDraftCandidateService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Sources.PurchaseDraftCandidateService>();
        // 独立采购草稿和确认使用独立服务；查询与预览通过只读边界执行。
        services.AddScoped<SwiftExport.Application.Services.Procurement.PurchaseOrders.Support.ProcurementOrderReadSupport>();
        services.AddScoped<SwiftExport.Application.Services.Procurement.PurchaseOrders.Confirmation.ProcurementConfirmationBuilder>();
        services.AddScoped<SwiftExport.Application.Services.Procurement.PurchaseOrders.Support.ProcurementOrderCommandSupport>();
        services.AddScoped<IProcurementOrderDraftService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.ProcurementOrderDraftService>();
        services.AddScoped<IProcurementOrderConfirmationService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Confirmation.ProcurementOrderConfirmationService>();
        services.AddScoped<IProcurementOrderReversalService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Reversal.ProcurementOrderReversalService>();
        services.AddScoped<IProcurementOrderQueryService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Query.ProcurementOrderQueryService>();
        services.AddScoped<IPurchaseOrderManagerQueryService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Query.PurchaseOrderManagerQueryService>();
        services.AddScoped<IPurchaseOrderProgressQueryService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Progress.PurchaseOrderProgressQueryService>();
        services.AddScoped<IPurchaseOrderProgressReportService, SwiftExport.Application.Services.Procurement.PurchaseOrders.Progress.PurchaseOrderProgressReportService>();
        services.AddScoped<IBomExecutionReader, SwiftExport.Application.Services.Products.Execution.BomExecutionReader>();
        services.AddScoped<SwiftExport.Application.Interfaces.MasterData.IExecutionEligibilityService, SwiftExport.Application.Services.MasterData.ExecutionEligibilityService>();
        services.AddScoped<SwiftExport.Application.Interfaces.MasterData.IProcurementMasterDataQueryService, SwiftExport.Application.Services.MasterData.ProcurementMasterDataQueryService>();
        services.AddScoped<IInventoryPostingService, InventoryPostingService>();
        services.AddScoped<AutomaticPostingScope>();
        services.AddScoped<IAutomaticPostingScope>(sp => sp.GetRequiredService<AutomaticPostingScope>());
        services.AddScoped<AutomaticPostingPorts>();
        services.AddScoped<IInboundPostingPort>(sp => sp.GetRequiredService<AutomaticPostingPorts>());
        services.AddScoped<IOutboundPostingPort>(sp => sp.GetRequiredService<AutomaticPostingPorts>());
        services.AddScoped<IWarehouseQueryService, WarehouseQueryService>();
        services.AddScoped<IWarehouseManagementService, WarehouseManagementService>();
        services.AddScoped<IInventoryQueryService, InventoryQueryService>();
        services.AddScoped<InboundServiceSupport>();
        services.AddScoped<IInboundQueryService, InboundQueryService>();
        services.AddScoped<IInboundSourceQueryService, InboundSourceQueryService>();
        services.AddScoped<IInboundDraftService, InboundDraftService>();
        services.AddScoped<IInboundPostingService, InboundPostingService>();
        services.AddScoped<IInboundReversalService, InboundReversalService>();
        services.AddScoped<OutboundServiceSupport>();
        services.AddScoped<IOutboundQueryService, OutboundQueryService>();
        services.AddScoped<IOutboundDraftService, OutboundDraftService>();
        services.AddScoped<IOutboundPostingService, OutboundPostingService>();
        services.AddScoped<IOutboundReversalService, OutboundReversalService>();
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
