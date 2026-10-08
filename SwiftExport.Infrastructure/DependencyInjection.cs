using SwiftExport.Core.Interfaces.Systems.Currencies;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;
using SwiftExport.Core.Interfaces.Systems.ExchangeRates;
using SwiftExport.Core.Interfaces.Systems.Numbering;
using SwiftExport.Infrastructure.Repositories.Systems.Currencies;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data;
using SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Types;
using SwiftExport.Infrastructure.Repositories.Systems.ExchangeRates;
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
        services.AddSingleton<SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier>(PostgreSqlExceptionClassifier.Instance);

        // 两个入口必须解析到同一个 scoped 实例，避免应用与仓储各持一条连接。
        services.AddScoped<SwiftExport.Infrastructure.Data.IDbConnectionFactory>(sp =>
            new PostgreSqlConnectionFactory(Microsoft.Extensions.Configuration.ConfigurationExtensions.GetConnectionString(
                sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), "Default")!));
        services.AddScoped<DbSession>(sp => new DbSession(sp.GetRequiredService<IDbConnectionFactory>()));
        services.AddScoped<SwiftExport.Core.Interfaces.Data.IDbSession>(sp => sp.GetRequiredService<DbSession>());
        services.AddScoped<RepositoryBaseContext>();

        // ══════════════════════════════════════════════════════
        // 认证相关服务
        // ══════════════════════════════════════════════════════
        // AuditStampService 依赖 ICurrentUser，ICurrentUser 在 WebApi 层注册（读 HttpContext）
        // Infrastructure 层只依赖接口，不依赖具体实现，保持分层干净
        services.AddScoped<IAuditStampService, AuditStampService>();
        services.AddScoped<SwiftExport.Infrastructure.Repositories.Auth.Development.PostgreSqlDevUserQuery>();
        // 已审计的旧SysUser查询无生产消费者，退出注册；保留源码及现行IAuthRepository认证链。
        services.AddScoped<IAuthRepository, PostgreSqlAuthRepository>();
        services.AddScoped<IUserPermissionRepository, PostgreSqlUserPermissionRepository>();
        services.AddScoped<IRbacManagementRepository, PostgreSqlRbacManagementRepository>();


        services.AddScoped(typeof(IRepository<>), typeof(PostgreSqlDapperRepository<>));
        services.AddScoped<IProductRepository, PostgreSqlProductRepository>();
        services.AddScoped<IProductMasterRepository, PostgreSqlProductMasterRepository>();
        services.AddScoped<IProductNeutralRepository, PostgreSqlProductNeutralRepository>();
        services.AddScoped<IProductNeutralManagerRepository, PostgreSqlProductNeutralManagerRepository>();
        services.AddScoped<IBomRepository, PostgreSqlBomRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Products.BomManagement.IBomManagementQueryRepository, SwiftExport.Infrastructure.Repositories.Products.BomManagement.PostgreSqlBomManagementQueryRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Products.BomManagement.IProductBomBindingRepository, SwiftExport.Infrastructure.Repositories.Products.BomManagement.PostgreSqlProductBomBindingRepository>();
        services.AddScoped<ICustomerRepository, PostgreSqlCustomerRepository>();
        services.AddScoped<ICustomerPoConfigRepository, PostgreSqlCustomerPoConfigRepository>();
        // 注册客户产品仓储
        services.AddScoped<ICustomerProductRepository, PostgreSqlCustomerProductRepository>();
        services.AddScoped<ISupplierRepository, PostgreSqlSupplierRepository>();
        services.AddScoped<ISupplierProductRepository, PostgreSqlSupplierProductRepository>();
        services.AddScoped<IProductColorRepository, PostgreSqlProductColorRepository>();
        services.AddScoped<IProductCategoryRepository, PostgreSqlProductCategoryRepository>();
        services.AddScoped<ISysDicCurrencyRepository, PostgreSqlSysDicCurrencyRepository>();
        services.AddScoped<ISysDicCurrencyExchangeRepository, PostgreSqlSysDicCurrencyExchangeRepository>();
        services.AddScoped<IDocumentSequenceRepository, PostgreSqlDocumentSequenceRepository>();
        services.AddScoped<ISysDictDataRepository, PostgreSqlSysDictDataRepository>();
        services.AddScoped<ISysDictTypeRepository, PostgreSqlSysDictTypeRepository>();
        services.AddScoped<ISalesOrderLineRepository, PostgreSqlSalesOrderLineRepository>();
        services.AddScoped<INetDemandExecutionLinkRepository, PostgreSqlNetDemandExecutionLinkRepository>();
        services.AddScoped<IGrossDemandQueryRepository, PostgreSqlGrossDemandQueryRepository>();
        services.AddScoped<IGrossDemandDocumentRepository, PostgreSqlGrossDemandDocumentRepository>();
        services.AddScoped<IMaterialDemandQueryRepository, PostgreSqlMaterialDemandQueryRepository>();
        services.AddScoped<IMaterialDemandSourceRepository, PostgreSqlMaterialDemandSourceRepository>();
        services.AddScoped<IMaterialDemandDocumentRepository, PostgreSqlMaterialDemandDocumentRepository>();
        services.AddScoped<IPurchaseOrderDraftRepository, PostgreSqlPurchaseOrderDraftRepository>();
        // 采购事务持久化与管理投影使用同一请求DbSession，不接回无生产消费者的旧递归BOM链。
        services.AddScoped<IProcurementOrderDocumentRepository, PostgreSqlProcurementOrderDocumentRepository>();
        services.AddScoped<IProcurementReferenceReadRepository, PostgreSqlProcurementReferenceReadRepository>();
        services.AddScoped<IProcurementReversalRepository, PostgreSqlProcurementReversalRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Procurement.IProcurementDemandAssociationRepository, SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Association.PostgreSqlProcurementDemandAssociationRepository>();
        services.AddScoped<IProcurementOrderFactLockRepository>(sp => sp.GetRequiredService<IProcurementReversalRepository>());
        services.AddScoped<IProcurementOrderQueryRepository, PostgreSqlProcurementOrderQueryRepository>();
        services.AddScoped<IPurchaseOrderManagerQueryRepository, PostgreSqlPurchaseOrderManagerQueryRepository>();
        services.AddScoped<IPurchaseOrderTraceRepository, SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace.PostgreSqlPurchaseOrderTraceRepository>();
        services.AddScoped<IPurchaseOrderProgressQueryRepository, PostgreSqlPurchaseOrderProgressQueryRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.MasterData.IExecutionEligibilityRepository, SwiftExport.Infrastructure.Repositories.MasterData.PostgreSqlExecutionEligibilityRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.MasterData.IProcurementMasterDataQueryRepository, SwiftExport.Infrastructure.Repositories.MasterData.PostgreSqlProcurementMasterDataQueryRepository>();
        services.AddScoped<ISupplierProgressReportRepository, PostgreSqlSupplierProgressReportRepository>();
        services.AddScoped<IBusinessOperationLogRepository, PostgreSqlBusinessOperationLogRepository>();
        services.AddScoped<ICommandReceiptRepository, PostgreSqlCommandReceiptRepository>();
        services.AddScoped<ISalesOrderHeadRepository, PostgreSqlSalesOrderHeadRepository>();
        services.AddScoped<ISalesOrderQueryRepository, PostgreSqlSalesOrderQueryRepository>();
        services.AddScoped<IWarehouseQueryRepository, PostgreSqlWarehouseQueryRepository>();
        services.AddScoped<IWarehouseManagementRepository, PostgreSqlWarehouseManagementRepository>();
        services.AddScoped<IWarehouseCandidateRepository, PostgreSqlWarehouseCandidateRepository>();
        services.AddScoped<IInventoryQueryRepository, PostgreSqlInventoryQueryRepository>();
        services.AddScoped<IInventoryPostingRepository, PostgreSqlInventoryPostingRepository>();
        services.AddScoped<IInboundDocumentRepository, PostgreSqlInboundDocumentRepository>();
        services.AddScoped<IInboundExecutionReadRepository, PostgreSqlInboundExecutionReadRepository>();
        services.AddScoped<IInboundQueryRepository, PostgreSqlInboundQueryRepository>();
        services.AddScoped<IInboundSourceRepository, PostgreSqlInboundSourceRepository>();
        services.AddScoped<IAutomaticDocumentRepository, PostgreSqlAutomaticDocumentRepository>();
        services.AddScoped<IOutboundQueryRepository, PostgreSqlOutboundQueryRepository>();
        services.AddScoped<IOutboundDocumentRepository, PostgreSqlOutboundDocumentRepository>();
        services.AddScoped<IOutboundExecutionReadRepository, PostgreSqlOutboundExecutionReadRepository>();
        services.AddScoped<PostgreSqlPackingListRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListQueryRepository>(sp => sp.GetRequiredService<PostgreSqlPackingListRepository>());
        services.AddScoped<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListDocumentRepository>(sp => sp.GetRequiredService<PostgreSqlPackingListRepository>());
        services.AddScoped<SwiftExport.Core.Interfaces.Shipping.PackingLists.IPackingListSourceExitRepository>(sp => sp.GetRequiredService<PostgreSqlPackingListRepository>());
        services.AddScoped<IPackingListPostingFactsRepository, PostgreSqlPackingListPostingFactsRepository>();
        services.AddScoped<IPackingListExecutionRepository, PostgreSqlPackingListExecutionRepository>();
        services.AddScoped<ISourceDeliveryQueryRepository, PostgreSqlSourceDeliveryQueryRepository>();

        services.AddScoped<SwiftExport.Core.Interfaces.QualityControl.Categories.IAfterSalesQualityCategoryRepository, SwiftExport.Infrastructure.Repositories.QualityControl.Categories.PostgreSqlAfterSalesQualityCategoryRepository>();
        services.AddScoped<IAfterSalesDocumentRepository, PostgreSqlAfterSalesDocumentRepository>();
        services.AddScoped<IAfterSalesQueryRepository, PostgreSqlAfterSalesQueryRepository>();
        services.AddScoped<IAfterSalesLookupRepository, PostgreSqlAfterSalesLookupRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.ForExcel.IForExcelSalesOrderRepository, SwiftExport.Infrastructure.Repositories.ForExcel.PostgreSqlForExcelSalesOrderRepository>();
        services.AddScoped<SwiftExport.Core.Interfaces.ForExcel.IForExcelPurchaseOrderRepository, SwiftExport.Infrastructure.Repositories.ForExcel.PostgreSqlForExcelPurchaseOrderRepository>();
        // 主配置和字段共享当前请求的 DbSession，由应用层统一控制事务。
        services.AddScoped<SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileRepository,
            SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles.PostgreSqlExcelReadProfileRepository>();
        return services;
    }
}
