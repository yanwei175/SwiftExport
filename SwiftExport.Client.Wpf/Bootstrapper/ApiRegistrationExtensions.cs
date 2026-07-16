using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Refit;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
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
        services.AddControllerApi<ISalesOrderWorkspaceApi>("sales-orders/workspace");
        services.AddControllerApi<IGrossDemandWorkspaceApi>("demand-planning/gross-demands");
        services.AddControllerApi<IDependentDemandApi>("DependentDemand");
        services.AddControllerApi<IPurchaseOrderApi>("PurchaseOrder");
        services.AddControllerApi<IFollowUpApi>("FollowUp");
        services.AddControllerApi<IReceiveApi>("Receive");
        services.AddControllerApi<IPackingListApi>("PackingList");
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
