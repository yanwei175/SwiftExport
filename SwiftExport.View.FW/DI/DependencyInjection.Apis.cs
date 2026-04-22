using Microsoft.Extensions.DependencyInjection;
using Refit;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.View.FW.Http;
using System;
using System.Net.Http;

namespace SwiftExport.View.FW
{
    /// <summary>
    /// Refit API 注册扩展方法。
    /// 统一配置 BaseAddress + 超时 + HTTP 日志拦截器。
    /// </summary>
    public static class RefitExtensions
    {
        /// <summary>
        /// 注册一个 Refit API 客户端，自动挂载 HTTP 日志拦截器。
        /// </summary>
        public static IHttpClientBuilder AddErpApi<T>(
            this IServiceCollection services, string baseAddress) where T : class
        {
            return services.AddRefitClient<T>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(baseAddress);
                    // 离线模式下缩短超时，避免点击后长时间无响应
                    c.Timeout = AppRuntimeOptions.DisableRemoteApi
                        ? TimeSpan.FromSeconds(2)
                        : TimeSpan.FromSeconds(30);
                })
                .AddHttpMessageHandler<HttpLoggingHandler>();
                // 启用 Token 注入时取消以下注释：
                // .AddHttpMessageHandler<AuthTokenHandler>();
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
        private static IServiceCollection AddViewApis(this IServiceCollection services)
        {
            // HTTP 日志拦截器（Transient：每次 HttpClient 创建时注入一个新实例）
            services.AddTransient<HttpLoggingHandler>();
            // Token 注入拦截器（预留，启用时取消注释）
            // services.AddTransient<AuthTokenHandler>();

            // TODO：后续从 appsettings / 配置中心读取，避免硬编码
            const string apiBaseUrl = "https://localhost:7086";

            // ================== 系统模块 ==================
            services.AddErpApi<ISysDictTypeApi>(apiBaseUrl + "/api/SysDictType");
            services.AddErpApi<ISysDictDataApi>(apiBaseUrl + "/api/SysDictData");
            services.AddErpApi<ISysDicCurrencyApi>(apiBaseUrl + "/api/SysDicCurrency");
            services.AddErpApi<ISysDicCurrencyExchangeApi>(apiBaseUrl + "/api/SysDicCurrencyExchange");
            services.AddErpApi<ISysViewRouteApi>(apiBaseUrl + "/api/SysViewRoute");

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

            return services;
        }
    }
}
