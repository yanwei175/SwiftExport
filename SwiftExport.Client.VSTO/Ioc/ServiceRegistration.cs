using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Bootstrapper;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Hosting.Windows;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Client.VSTO.Features.Sales.Orders;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Views;
using SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views;

namespace SwiftExport.Client.VSTO.Ioc
{
    /// <summary>插件唯一服务注册入口，不通过业务页面访问全局容器。</summary>
    public static class ServiceRegistration
    {
        /// <summary>注册认证与按需缓存；构造服务不预热资料，外部调度器由 Bootstrapper 释放。</summary>
        public static ServiceProvider Build(ApiOptions options, ExcelDispatcher dispatcher, AddInLog log)
        {
            var services = new ServiceCollection();
            services.AddSingleton(log);
            services.AddSingleton<IUiDispatcher>(dispatcher);
            services.AddSingleton<IExcelDispatcher>(dispatcher);
            services.AddSingleton<ITokenStore>(new ProtectedTokenStore(options.BaseAddress, log));
            services.AddSingleton<SessionService>();
            services.AddSingleton<CurrentUserService>();
            services.AddSingleton(provider => new ApiClientRegistration(options.BaseAddress, provider.GetRequiredService<SessionService>()));
            services.AddSingleton<IAuthGateway>(provider => provider.GetRequiredService<ApiClientRegistration>());
            services.AddSingleton<AuthService>();
            services.AddSingleton<ApiCallRunner>();
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<ICustomerApi>("api/Customer"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<ISupplierApi>("api/Supplier"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<IWarehouseSiteApi>("api/inventory/warehouse-sites"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<IProductColorApi>("api/ProductColor"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<IProductCategoryApi>("api/ProductCategory"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<ISysDicCurrencyApi>("api/SysDicCurrency"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<ISysDictTypeApi>("api/SysDictType"));
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<ISysDictDataApi>("api/SysDictData"));
            services.AddSingleton<IReferenceDataCacheService, ReferenceDataCacheService>();
            services.AddSingleton(p => p.GetRequiredService<ApiClientRegistration>().Create<ISalesOrderDetailApi>("api/sales-orders/detail"));
            services.AddSingleton(TaskPaneRegistrar.CreateDefault());
            services.AddScoped<TaskPaneContext>();
            services.AddScoped<SalesOrderImportPrompt>();
            services.AddScoped<ISalesOrderImportPrompt>(p => p.GetRequiredService<SalesOrderImportPrompt>());
            services.AddScoped<SalesOrderImportViewModel>();
            services.AddScoped<SalesOrderImportView>();
            services.AddScoped<PurchaseOrderImportViewModel>();
            services.AddScoped<PurchaseOrderImportView>();
            services.AddSingleton<WindowManager>();
            services.AddSingleton<LoginCoordinator>();
            services.AddScoped<LoginViewModel>();
            services.AddScoped<LoginWindow>();
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }
}
