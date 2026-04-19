using Microsoft.Extensions.DependencyInjection;
using Refit;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.CustomerViews.ViewModels;
using SwiftExport.View.FW.CustomerViews.Views;
using SwiftExport.View.FW.DictViews.ViewModels;
using SwiftExport.View.FW.DictViews.Views;
using SwiftExport.View.FW.Mappings;
using SwiftExport.View.FW.ProductViews.ViewModels;
using SwiftExport.View.FW.ProductViews.Views;
using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using SwiftExport.View.FW.SalesOrderViews.Views;
using SwiftExport.View.FW.Services;
using SwiftExport.View.FW.SupplierViews.ViewModels;
using SwiftExport.View.FW.SupplierViews.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW
{
    /// <summary>
    /// 中文注释：前端运行时选项。
    /// </summary>
    public static class AppRuntimeOptions
    {
        /// <summary>
        /// 中文注释：离线模式开关。
        /// true 时表示当前不依赖后端联调，启动阶段不应主动触发远程调用。
        /// </summary>
        public static bool DisableRemoteApi { get; set; } = false;
    }
    /// <summary>
    ///  中文注释：Token 拦截器。
    /// </summary>
    public class AuthTokenHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 1. 每次请求前，去获取最新的 Token （比如从本地缓存或你的全局变量里取）
            string latestToken = "这里去获取你最新的Token";

            // 2. 塞入请求头
            if (!string.IsNullOrEmpty(latestToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", latestToken);
            }

            // 3. 放行请求
            return await base.SendAsync(request, cancellationToken);
        }
    }
    /// <summary>
    /// 中文注释：Refit API 注册扩展方法。
    /// </summary>
    public static class RefitExtensions
    {
        public static IHttpClientBuilder AddErpApi<T>(this IServiceCollection services, string baseAddress) where T : class
        {
            return services.AddRefitClient<T>()
                .ConfigureHttpClient(c =>
                {
                    c.BaseAddress = new Uri(baseAddress);
                    // 中文注释：离线模式下使用更短超时，避免点击功能后长时间等待。
                    c.Timeout = AppRuntimeOptions.DisableRemoteApi ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(30);
                })
            .AddHttpMessageHandler<HttpLoggingHandler>();
            // ★ 挂载 Token 拦截器！这样每次请求都会经过 AuthTokenHandler
            //.AddHttpMessageHandler<AuthTokenHandler>();
        }
    }
    /// <summary>
    /// 中文注释：前端依赖注入注册类。
    /// </summary>
    public static class DI_View
    {
        public static IServiceCollection AddViewServices(this IServiceCollection services)
        {
            //注入 Mapster 映射配置
            MapsterConfig.RegisterMappings(services);

            //注入弹窗服务
            services.AddSingleton<IDialogService, DialogService>();

            //注入字典缓存服务
            services.AddSingleton<IDictCacheService, DictCacheService>();


            //// ================== 拦截器注册 ==================
            //services.AddTransient<AuthTokenHandler>();
            services.AddTransient<HttpLoggingHandler>();
            // ================== API 接口注册 ==================
            string apiBaseUrl = "https://localhost:7086";
            services.AddErpApi<ISysDictTypeApi>(apiBaseUrl + "/api/SysDictType");
            services.AddErpApi<ISysDictDataApi>(apiBaseUrl + "/api/SysDictData");
            //services.AddErpApi<IDictApi>(apiBaseUrl + "/api/Dict");

            services.AddErpApi<IProductApi>(apiBaseUrl + "/api/Product");
            services.AddErpApi<IProductColorApi>(apiBaseUrl + "/api/ProductColor");
            services.AddErpApi<IProductCategoryApi>(apiBaseUrl + "/api/ProductCategory");

            services.AddErpApi<IBomApi>(apiBaseUrl + "/api/Bom");

            services.AddErpApi<ISupplierApi>(apiBaseUrl + "/api/Supplier");

            services.AddErpApi<ICustomerApi>(apiBaseUrl + "/api/Customer");
            services.AddErpApi<ICustomerPoConfigApi>(apiBaseUrl + "/api/CustomerPoConfig");
            services.AddErpApi<ICustomerProductApi>(apiBaseUrl + "/api/CustomerProduct");

            // 中文注释：销售订单 API 注册
            services.AddErpApi<ISalesOrderApi>(apiBaseUrl + "/api/SalesOrder");

            // ================== ViewModel & View 注册 ==================
            services.AddTransient<ProductSelectorViewModel>();

            services.AddTransient<CustomerManagerViewModel>();
            services.AddTransient<CustomerManagerView>();
            services.AddTransient<CustomerManagerViewModel>();
            services.AddTransient<CustomerManagerView>();

            services.AddTransient<CustomerPoConfigViewModel>();
            services.AddTransient<CustomerPoConfigManagerView>();

            services.AddTransient<SupplierManagerViewModel>();
            services.AddTransient<SupplierManagerView>();

            services.AddTransient<ProductColorManagerViewModel>();
            services.AddTransient<ProductColorManagerView>();

            services.AddTransient<DictTypeManagerViewModel>();
            services.AddTransient<DictTypeManagerView>();

            services.AddTransient<DictDataManagerViewModel>();
            services.AddTransient<DictDataManagerView>();

            services.AddTransient<DictManagerViewModel>();
            services.AddTransient<DictManagerView>();

            services.AddTransient<DataDictManagerViewModel>();
            services.AddTransient<DataDictManagerDesignVM>();
            services.AddTransient<DataDictManagerView>();

            services.AddTransient<ProductCategoryManagerViewModel>();
            services.AddTransient<ProductCategoryManagerView>();

            services.AddTransient<ProductManagerViewModel>();
            services.AddTransient<ProductManagerView>();

            // 中文注释：销售订单管理 ViewModel 和 View 注册
            services.AddTransient<SalesOrderManagerViewModel>();
            services.AddTransient<SalesOrderDetailViewModel>();
            services.AddTransient<SalesOrderManagerView>();
            services.AddTransient<SalesOrderDetailView>();

            return services;
        }
    }

    /// <summary>
    /// 中文注释：HTTP 日志拦截器。
    /// </summary>
    public class HttpLoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 1. 请求发出去之前：拦截并打印极其真实的绝对路径！
            Debug.WriteLine("=================================================");
            Debug.WriteLine($"[HTTP 发送请求] {request.Method} {request.RequestUri}");
            Debug.WriteLine("=================================================");

            // 2. 放行请求，让它真正发到服务器去
            var response = await base.SendAsync(request, cancellationToken);

            // 3. 响应回来之后：打印状态码
            Debug.WriteLine($"[HTTP 收到响应] 状态码: {(int)response.StatusCode} ({response.StatusCode})");

            // 如果你想看后端返回的具体错误信息（比如详细的 404 或 500 报错），可以解开下面的注释：
            // if (!response.IsSuccessStatusCode && response.Content != null)
            // {
            //     var errorContent = await response.Content.ReadAsStringAsync();
            //     Debug.WriteLine($"[HTTP 错误详情] {errorContent}");
            // }

            return response;
        }
    }






}

