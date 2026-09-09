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
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Bootstrapper;

public static class ApiRegistrationExtensions
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(15);

    public static IServiceCollection AddSwiftExportApis(this IServiceCollection services)
    {
        services.AddRefitClient<IAuthApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = AuthTimeout;
            })
            .AddHttpMessageHandler<AuthTokenHandler>();

        services.AddRefitClient<IClientVersionApi>()
            .ConfigureHttpClient((provider, client) =>
            {
                client.BaseAddress = BuildSiteRoot(provider);
                client.Timeout = AuthTimeout;
            });

        services.AddSystemApis();
        services.AddProductApis();
        services.AddSupplierApis();
        services.AddCustomerApis();
        services.AddOrderApis();
        services.AddInventoryApis();
        services.AddQualityControlApis();

        return services;
    }

    private static void AddSystemApis(this IServiceCollection services)
    {
        services.AddControllerApi<ISysDictTypeApi>("SysDictType");
        services.AddControllerApi<ISysDictDataApi>("SysDictData");
        services.AddControllerApi<IDictApi>("Dict");
        services.AddControllerApi<ISysDicCurrencyApi>("SysDicCurrency");
        services.AddControllerApi<ISysDicCurrencyExchangeApi>("SysDicCurrencyExchange");
        services.AddControllerApi<ISysViewRouteApi>("SysViewRoute");
        services.AddControllerApi<IDocumentNoApi>("DocumentNo");
    }

    private static void AddProductApis(this IServiceCollection services)
    {
        services.AddControllerApi<IProductApi>("Product");
        services.AddControllerApi<IProductMasterApi>("ProductMaster");
        services.AddControllerApi<IProductNeutralApi>("ProductNeutral");
        services.AddControllerApi<IProductNeutralManagerApi>("ProductNeutralManager");
        services.AddControllerApi<IProductColorApi>("ProductColor");
        services.AddControllerApi<IProductCategoryApi>("ProductCategory");
        services.AddControllerApi<IBomApi>("Bom");
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
        services.AddControllerApi<ISalesOrderDetailApi>("sales-orders/detail");
        services.AddControllerApi<ISalesFulfillmentManagerApi>("sales-fulfillment/manager");
        services.AddControllerApi<ISalesFulfillmentDetailApi>("sales-fulfillment/detail");
        services.AddControllerApi<IGrossDemandWorkspaceApi>("demand-planning/gross-demands");
        services.AddControllerApi<INetDemandWorkspaceApi>("demand-planning/net-demands");
        services.AddControllerApi<IMaterialDemandWorkspaceApi>("demand-planning/net-demands");
        services.AddControllerApi<IPurchaseOrderWorkspaceApi>("procurement/purchase-orders");
        services.AddControllerApi<IProcurementOrderApi>("procurement/orders");
        services.AddControllerApi<IPurchaseOrderManagerApi>("procurement/order-manager");
        services.AddControllerApi<IProcurementMasterDataQueryApi>("master-data/procurement-lookups");
    }

    private static void AddInventoryApis(this IServiceCollection services)
    {
        services.AddControllerApi<IInventoryApi>("inventory");
        services.AddControllerApi<IWarehouseSiteApi>("inventory/warehouse-sites");
        services.AddControllerApi<IInboundWorkspaceApi>("inventory/inbounds");
        services.AddControllerApi<IOutboundWorkspaceApi>("inventory/outbounds");
        services.AddControllerApi<IPackingListWorkspaceApi>("shipping/packing-lists");
    }

    /// <summary>注册品质管理 API；售后契约使用站点根路径上的绝对路由。</summary>
    private static void AddQualityControlApis(this IServiceCollection services)
    {
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
