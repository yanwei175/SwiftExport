using SwiftExport.Core.Interfaces.Systems.Currencies;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Systems.ExchangeRates;
using SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;
using SwiftExport.Core.Interfaces.Systems.Numbering;
using SwiftExport.Infrastructure.Repositories.Systems.Currencies;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;
using SwiftExport.Infrastructure.Repositories.Systems.ExchangeRates;
using SwiftExport.Infrastructure.Repositories.Systems.ExcelFieldMappings;
using SwiftExport.Infrastructure.Repositories.Systems.Numbering;
using SwiftExport.Core.Interfaces.Customers.CustomerProducts;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Suppliers.SupplierProducts;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Document;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Query;
using SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Document;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Sources;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Execution;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Core.Interfaces.Inventory.Query;
using SwiftExport.Core.Interfaces.Inventory.Posting;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth;
using SwiftExport.Infrastructure.Repositories.Auth.Accounts;
using SwiftExport.Infrastructure.Repositories.Auth.Permissions;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Customers;
using SwiftExport.Infrastructure.Repositories.DemandPlanning;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Products.ProductMasters;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Repositories.Products.Colors;
using SwiftExport.Infrastructure.Repositories.Products.Categories;
using SwiftExport.Infrastructure.Repositories.Customers.Management;
using SwiftExport.Infrastructure.Repositories.Customers.CustomerProducts;
using SwiftExport.Infrastructure.Repositories.Customers.PoConfigs;
using SwiftExport.Infrastructure.Repositories.Suppliers.Management;
using SwiftExport.Infrastructure.Repositories.Suppliers.SupplierProducts;
using SwiftExport.Infrastructure.Repositories.Procurement;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Reversal;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.ReferenceRead;
using SwiftExport.Infrastructure.Repositories.Suppliers;
using SwiftExport.Infrastructure.Repositories.Systems;
using SwiftExport.Infrastructure.Repositories.Inventory;
using SwiftExport.Infrastructure.Repositories.Inventory.AutomaticDocuments;
using SwiftExport.Infrastructure.Repositories.Inventory.Query;
using SwiftExport.Infrastructure.Repositories.Inventory.Posting;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
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
        services.AddScoped<IRbacManagementRepository, RbacManagementRepository>();


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
        services.AddScoped<IExcelFieldMappingRepository, ExcelFieldMappingRepository>();
        services.AddScoped<ISalesOrderLineRepository, SalesOrderLineRepository>();
        services.AddScoped<INetDemandExecutionLinkRepository, NetDemandExecutionLinkRepository>();
        services.AddScoped<IGrossDemandQueryRepository, GrossDemandQueryRepository>();
        services.AddScoped<IGrossDemandDocumentRepository, GrossDemandDocumentRepository>();
        services.AddScoped<IMaterialDemandQueryRepository, MaterialDemandQueryRepository>();
        services.AddScoped<IMaterialDemandSourceRepository, MaterialDemandSourceRepository>();
        services.AddScoped<IMaterialDemandDocumentRepository, MaterialDemandDocumentRepository>();
        services.AddScoped<IPurchaseOrderDraftRepository, PurchaseOrderDraftRepository>();
        // 新链的事务持久化、管理投影与严格 BOM 读取使用同一请求 DbSession。
        services.AddScoped<IProcurementOrderDocumentRepository, ProcurementOrderDocumentRepository>();
        services.AddScoped<IProcurementReferenceReadRepository, ProcurementReferenceReadRepository>();
        services.AddScoped<IProcurementReversalRepository, ProcurementReversalRepository>();
        services.AddScoped<IProcurementOrderFactLockRepository>(sp => sp.GetRequiredService<IProcurementReversalRepository>());
        services.AddScoped<IProcurementOrderQueryRepository, ProcurementOrderQueryRepository>();
        services.AddScoped<IPurchaseOrderManagerQueryRepository, PurchaseOrderManagerQueryRepository>();
        services.AddScoped<IPurchaseOrderTraceRepository, SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace.PurchaseOrderTraceRepository>();
        services.AddScoped<IPurchaseOrderProgressQueryRepository, PurchaseOrderProgressQueryRepository>();
        services.AddScoped<IBomExecutionRepository, BomExecutionRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.MasterData.IExecutionEligibilityRepository, SwiftExport.Infrastructure.Repositories.MasterData.ExecutionEligibilityRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.MasterData.IProcurementMasterDataQueryRepository, SwiftExport.Infrastructure.Repositories.MasterData.ProcurementMasterDataQueryRepository>();
        services.AddScoped<ISupplierProgressReportRepository, SupplierProgressReportRepository>();
        services.AddScoped<IBusinessOperationLogRepository, BusinessOperationLogRepository>();
        services.AddScoped<ICommandReceiptRepository, CommandReceiptRepository>();
        services.AddScoped<ISalesOrderHeadRepository, SalesOrderHeadRepository>();
        services.AddScoped<ISalesOrderQueryRepository, SalesOrderQueryRepository>();
        services.AddScoped<IWarehouseQueryRepository, WarehouseQueryRepository>();
        services.AddScoped<IWarehouseManagementRepository, WarehouseManagementRepository>();
        services.AddScoped<IWarehouseCandidateRepository, WarehouseCandidateRepository>();
        services.AddScoped<IInventoryQueryRepository, InventoryQueryRepository>();
        services.AddScoped<IInventoryPostingRepository, InventoryPostingRepository>();
        services.AddScoped<IInboundDocumentRepository, InboundDocumentRepository>();
        services.AddScoped<IInboundExecutionReadRepository, InboundExecutionReadRepository>();
        services.AddScoped<IInboundQueryRepository, InboundQueryRepository>();
        services.AddScoped<IInboundSourceRepository, InboundSourceRepository>();
        services.AddScoped<IAutomaticDocumentRepository, AutomaticDocumentRepository>();
        services.AddScoped<IOutboundQueryRepository, OutboundQueryRepository>();
        services.AddScoped<IOutboundDocumentRepository, OutboundDocumentRepository>();
        services.AddScoped<IOutboundExecutionReadRepository, OutboundExecutionReadRepository>();
        services.AddScoped<PackingListRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListQueryRepository>(sp => sp.GetRequiredService<PackingListRepository>());
        services.AddScoped<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListDocumentRepository>(sp => sp.GetRequiredService<PackingListRepository>());
        services.AddScoped<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListSourceExitRepository>(sp => sp.GetRequiredService<PackingListRepository>());
        services.AddScoped<IPackingListPostingFactsRepository, PackingListPostingFactsRepository>();
        services.AddScoped<IPackingListExecutionRepository, PackingListExecutionRepository>();
        services.AddScoped<ISourceDeliveryQueryRepository, SourceDeliveryQueryRepository>();

        services.AddScoped<IAfterSalesDocumentRepository, AfterSalesDocumentRepository>();
        services.AddScoped<IAfterSalesQueryRepository, AfterSalesQueryRepository>();
        services.AddScoped<IAfterSalesLookupRepository, AfterSalesLookupRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.ForExcel.IForExcelSalesOrderRepository, SwiftExport.Infrastructure.Repositories.ForExcel.ForExcelSalesOrderRepository>();
        // 主配置和字段共享当前请求的 DbSession，由应用层统一控制事务。
        services.AddScoped<SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileRepository,
            SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles.ExcelReadProfileRepository>();
        return services;
    }
}
