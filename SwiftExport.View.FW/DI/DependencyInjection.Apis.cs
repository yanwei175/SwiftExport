using Microsoft.Extensions.DependencyInjection;
using Refit;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.View.FW.Http;
using System;

namespace SwiftExport.View.FW
{
    /// <summary>
    /// Refit API 注册扩展方法。
    /// 统一配置 BaseAddress + 超时 + HTTP 日志拦截器。
    /// </summary>
    public static class RefitExtensions
    {
        /// <summary>
        /// 注册一个 Refit API 客户端，自动挂载日志拦截器和 Token 注入拦截器。
        /// </summary>
        public static IHttpClientBuilder AddErpApi<T>(
            this IServiceCollection services, string baseAddress) where T : class
        {
            return services.AddRefitClient<T>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(baseAddress);
                    c.Timeout = AppRuntimeOptions.DisableRemoteApi
                        ? TimeSpan.FromSeconds(2)
                        : TimeSpan.FromSeconds(30);
                })
                .AddHttpMessageHandler<HttpLoggingHandler>()
                .AddHttpMessageHandler<AuthTokenHandler>(); // ← Token 已启用，登录后自动带 Bearer
        }
    }

    public static partial class DependencyInjection
    {
        /// <summary>
        /// 注册所有 Refit HTTP API 接口及 HTTP 拦截器。
        ///
        /// 规则：
        ///   - 每个 API 接口对应后端一个 Controller，URL 段与 Controller 路由一致
        ///   - apiBaseUrl 统一在此处维护，后续替换为配置文件读取
        ///
        /// 新增 API 时：在对应业务分组末尾追加一行 services.AddErpApi 即可。
        /// </summary>
        public static IServiceCollection AddViewApis(this IServiceCollection services)
        {
            // HTTP 日志拦截器（Transient：每次 HttpClient 创建时注入一个新实例）
            services.AddTransient<HttpLoggingHandler>();
            // Token 注入拦截器（已启用：登录后自动为所有 API 请求附加 Bearer Token）
            services.AddTransient<AuthTokenHandler>();

            // TODO：后续从 appsettings / 配置中心读取，避免硬编码
            const string apiBaseUrl = "https://localhost:7286";

            // ================== 认证模块（无需 BaseUrl 前缀，单独注册）==================
            services.AddRefitClient<IAuthApi>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(apiBaseUrl);
                    c.Timeout = TimeSpan.FromSeconds(15);
                })
                .AddHttpMessageHandler<HttpLoggingHandler>();
            // 注意：IAuthApi 的 login 接口不挂 AuthTokenHandler，避免循环依赖

            // ================== 系统模块 ==================
            services.AddErpApi<ISysDictTypeApi>(apiBaseUrl + "/api/SysDictType");
            services.AddErpApi<ISysDictDataApi>(apiBaseUrl + "/api/SysDictData");
            services.AddErpApi<ISysDicCurrencyApi>(apiBaseUrl + "/api/SysDicCurrency");
            services.AddErpApi<ISysDicCurrencyExchangeApi>(apiBaseUrl + "/api/SysDicCurrencyExchange");
            services.AddErpApi<ISysViewRouteApi>(apiBaseUrl + "/api/SysViewRoute");
            services.AddErpApi<IDocumentNoApi>(apiBaseUrl + "/api/DocumentNo");

            // ================== 产品模块 ==================
            services.AddErpApi<IProductApi>(apiBaseUrl + "/api/Product");
            services.AddErpApi<IProductColorApi>(apiBaseUrl + "/api/ProductColor");
            services.AddErpApi<IProductCategoryApi>(apiBaseUrl + "/api/ProductCategory");
            services.AddErpApi<IBomApi>(apiBaseUrl + "/api/Bom");

            // ================== 供应商模块 ==================
            services.AddErpApi<ISupplierApi>(apiBaseUrl + "/api/Supplier");

            // ================== 客户模块 ==================
            services.AddErpApi<ICustomerApi>(apiBaseUrl + "/api/Customer");
            services.AddErpApi<ICustomerPoConfigApi>(apiBaseUrl + "/api/CustomerPoConfig");
            services.AddErpApi<ICustomerProductApi>(apiBaseUrl + "/api/CustomerProduct");

            // ================== 销售订单模块 ==================
            services.AddErpApi<ISalesOrderApi>(apiBaseUrl + "/api/SalesOrder");
            services.AddErpApi<IIndependentDemandApi>(apiBaseUrl + "/api/IndependentDemand");
            services.AddErpApi<IDependentDemandApi>(apiBaseUrl + "/api/DependentDemand");
            services.AddErpApi<IPurchaseOrderApi>(apiBaseUrl + "/api/PurchaseOrder");
            services.AddErpApi<IFollowUpApi>(apiBaseUrl + "/api/FollowUp");
            services.AddErpApi<IReceiveApi>(apiBaseUrl + "/api/Receive");
            services.AddErpApi<IPackingListApi>(apiBaseUrl + "/api/PackingList");

            return services;
        }
    }
}
