using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.Apis.Files;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files.ServerTemplates;

namespace SwiftExportClient.Bootstrapper;

/// <summary>集中注册按业务 Controller 基地址访问的 Refit 契约和客户端认证处理器。</summary>
public static class ApiRegistrationExtensions
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(15);

    /// <summary>注册客户端 API，包括 BOM 专用窄绑定，沿用公共会话认证和请求超时。</summary>
    public static IServiceCollection AddSwiftExportApis(this IServiceCollection services)
    {
        services.AddRefitClient<IAuthApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = AuthTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();

        services.AddRefitClient<ISystemRbacApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = DefaultTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();

        services.AddRefitClient<ISystemRbacApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = DefaultTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();

        services.AddRefitClient<IClientVersionApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = AuthTimeout;
            });

        services.AddSystemApis();
        services.AddControllerApi<IExportTemplateApi>("export-templates");
        // 下载仅缓冲响应头；正文读取的超时、容量和取消由服务器模板服务统一控制。
        services.AddHttpClient(ServerExcelTemplateService.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = new Uri(EnsureTrailingSlash(
                    BuildControllerRoot(provider, "export-templates").AbsoluteUri), UriKind.Absolute);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddProductApis();
        services.AddSupplierApis();
        services.AddCustomerApis();
        services.AddOrderApis();
        services.AddInventoryApis();
        services.AddQualityControlApis();
        // PackingList 前端契约使用控制器基址，后端接口仍待实现。
        services.AddControllerApi<SwiftExport.Contracts.Apis.Shipping.IPackingListApi>("shipping/packing-lists");

        return services;
    }

    private static void AddSystemApis(this IServiceCollection services)
    {
        services.AddControllerApi<ISystemParameterApi>("system-parameters");
        services.AddControllerApi<ISysDictTypeApi>("SysDictType");
        services.AddControllerApi<ISysDictDataApi>("SysDictData");
        services.AddControllerApi<ISysDicCurrencyApi>("SysDicCurrency");
        services.AddControllerApi<ISysDicCurrencyExchangeApi>("SysDicCurrencyExchange");
        services.AddControllerApi<IDocumentNoApi>("DocumentNo");
    }

    /// <summary>注册产品维护、图片、分类与标签定义 API，均沿用客户端会话认证。</summary>
    private static void AddProductApis(this IServiceCollection services)
    {
        // 图片处理包含上传、转码和磁盘发布，使用独立超时，不重试覆盖请求。
        services.AddControllerApi<SwiftExport.Contracts.Apis.Files.IProductImageApi>("product-images")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(5));
        services.AddControllerApi<IProductApi>("Product");
        services.AddControllerApi<IProductMasterApi>("ProductMaster");
        services.AddControllerApi<IProductMasterImportApi>("ProductMasterImport");
        services.AddTransient<Func<IProductMasterImportApi>>(provider => () => provider.GetRequiredService<IProductMasterImportApi>());
        services.AddControllerApi<IProductNeutralApi>("ProductNeutral");
        services.AddControllerApi<IProductNeutralManagerApi>("ProductNeutralManager");
        services.AddTransient<Func<IProductNeutralManagerApi>>(provider => () => provider.GetRequiredService<IProductNeutralManagerApi>());
        services.AddControllerApi<IProductColorApi>("ProductColor");
        services.AddControllerApi<IProductCategoryApi>("ProductCategory");
        services.AddControllerApi<IProductAttributeApi>("ProductAttribute");
        services.AddControllerApi<IBomApi>("Bom");
        services.AddControllerApi<IProductBomBindingApi>("Product");
    }

    private static void AddSupplierApis(this IServiceCollection services)
    {
        services.AddControllerApi<ISupplierApi>("Supplier");
        services.AddControllerApi<ISupplierProductApi>("SupplierProduct");
    }

    private static void AddCustomerApis(this IServiceCollection services)
    {
        services.AddControllerApi<ICustomerApi>("Customer");
        services.AddControllerApi<ICustomerPoConfigApi>("CustomerPoConfig");
        services.AddControllerApi<ICustomerProductApi>("CustomerProduct");
    }

    private static void AddOrderApis(this IServiceCollection services)
    {
        services.AddControllerApi<ISalesOrderManagerApi>("sales-orders/manager");
        services.AddControllerApi<SwiftExport.Contracts.Apis.OrderTracking.IOrderTrackingApi>("order-tracking");
        services.AddControllerApi<ISalesOrderDetailApi>("sales-orders/detail");
        services.AddControllerApi<IGrossDemandWorkspaceApi>("demand-planning/gross-demands");
        services.AddControllerApi<INetDemandManagerApi>("demand-planning/net-demands");
        services.AddControllerApi<IPurchaseDraftApi>("procurement/purchase-drafts");
        services.AddControllerApi<IProcurementOrderApi>("procurement/orders");
        services.AddControllerApi<IPurchaseOrderManagerApi>("procurement/order-manager");
        services.AddControllerApi<IPurchaseOrderTraceApi>("procurement/purchase-order-trace");
        services.AddControllerApi<IPurchaseOrderProgressApi>("procurement/purchase-order-progress");
        services.AddControllerApi<IProcurementMasterDataQueryApi>("master-data/procurement-lookups");
    }

    /// <summary>注册库存及入库管理、选源的独立 API 基址。</summary>
    private static void AddInventoryApis(this IServiceCollection services)
    {
        services.AddControllerApi<IInventoryApi>("inventory");
        services.AddControllerApi<IInboundSourceApi>("inventory/inbound-sources");
        services.AddControllerApi<IInboundManagerApi>("inventory/inbounds");
        services.AddControllerApi<IOutboundManagerApi>("inventory/outbounds");
        services.AddControllerApi<IWarehouseSiteApi>("inventory/warehouse-sites");
    }

    /// <summary>注册品质管理 API；售后和流量契约使用站点根路径上的绝对路由。</summary>
    private static void AddQualityControlApis(this IServiceCollection services)
    {
        services.AddRefitClient<SwiftExport.Contracts.Apis.QualityControl.FlowManagement.IFlowApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = DefaultTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddRefitClient<IAfterSalesQualityCategoryApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = DefaultTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();
        services.AddRefitClient<IAfterSalesApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = DefaultTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();
    }

    private static IHttpClientBuilder AddControllerApi<TApi>(
        this IServiceCollection services,
        string controllerName)
        where TApi : class
    {
        return services.AddRefitClient<TApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildControllerRoot(provider, controllerName);
                client.Timeout = DefaultTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();
    }

    private static Uri BuildSiteRoot(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<ApiOptions>>().Value;
        return new Uri(EnsureTrailingSlash(options.BaseUrl), UriKind.Absolute);
    }

    private static Uri BuildControllerRoot(IServiceProvider provider, string controllerName)
    {
        var root = BuildSiteRoot(provider).ToString().TrimEnd('/');
        return new Uri($"{root}/api/{controllerName}", UriKind.Absolute);
    }

    private static string EnsureTrailingSlash(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : $"{trimmed}/";
    }
}
