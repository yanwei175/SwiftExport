using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Interfaces.SalesOrders.Draft;
using SwiftExport.Application.Interfaces.SalesOrders.Confirmation;
using SwiftExport.Application.Interfaces.SalesOrders.Lifecycle;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Maintenance;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Lifecycle;
using SwiftExport.Core.Interfaces.SalesOrders.Query;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Infrastructure;
using SwiftExport.WebApi.Controllers.SalesOrders.Query;
using SwiftExport.WebApi.Controllers.SalesOrders.Draft;
using SwiftExport.WebApi.Controllers.SalesOrders.Confirmation;
using SwiftExport.WebApi.Controllers.SalesOrders.Lifecycle;
using SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Query;
using SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Maintenance;
using SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Confirmation;
using SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Lifecycle;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>按公开路径逐项核对销售与售后切片后的二十个 HTTP 动作及原防重复标记。</summary>
[TestClass]
public sealed class SalesAfterSalesRouteContractTests
{
    /// <summary>只检查注册描述符，不解析或连接数据库；旧宽接口不能再次出现。</summary>
    [TestMethod]
    public void DependencyInjection_RegistersNarrowServicesAndRepositories()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddInfrastructureServices();
        var expected = new[]
        {
            typeof(ISalesOrderQueryService), typeof(ISalesOrderDraftService), typeof(ISalesOrderConfirmationService),
            typeof(ISalesOrderCancellationService), typeof(ISalesOrderLifecycleService),
            typeof(IAfterSalesQueryService), typeof(IAfterSalesMaintenanceService), typeof(IAfterSalesConfirmationService),
            typeof(IAfterSalesCancellationService), typeof(IAfterSalesLifecycleService),
            typeof(ISalesOrderQueryRepository), typeof(ISalesOrderHeadRepository), typeof(ISalesOrderLineRepository),
            typeof(IAfterSalesQueryRepository), typeof(IAfterSalesLookupRepository), typeof(IAfterSalesDocumentRepository)
        };
        foreach (var contract in expected)
            Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == contract), contract.Name);
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType.Name is
            "ISalesOrderManagerService" or "ISalesOrderDetailService" or "IAfterSalesService" or "IAfterSalesRepository"));
    }

    /// <summary>销售十个动作保留两个旧根路径、动词及确认动作的独立认证。</summary>
    [TestMethod]
    public void SalesRoutes_KeepOriginalMethodsAndPaths()
    {
        var expected = new (Type Controller, string Method, string Verb, string Path, bool Debounce)[]
        {
            (typeof(SalesOrderQueryController), "SearchAsync", "POST", "api/sales-orders/manager/search", false),
            (typeof(SalesOrderQueryController), "GetCustomerCodesAsync", "GET", "api/sales-orders/manager/customer-codes", false),
            (typeof(SalesOrderQueryController), "GetAsync", "GET", "api/sales-orders/detail/{uid}", false),
            (typeof(SalesOrderQueryController), "SearchProductsAsync", "POST", "api/sales-orders/detail/products/search", false),
            (typeof(SalesOrderQueryController), "SearchCustomersAsync", "POST", "api/sales-orders/detail/customers/search", false),
            (typeof(SalesOrderDraftController), "SaveAsync", "POST", "api/sales-orders/detail/save", true),
            (typeof(SalesOrderDraftController), "DeleteDraftAsync", "POST", "api/sales-orders/detail/{uid}/delete-draft", true),
            (typeof(SalesOrderConfirmationController), "ConfirmAsync", "POST", "api/sales-orders/detail/{uid}/confirm", true),
            (typeof(SalesOrderConfirmationController), "CancelConfirmationAsync", "POST", "api/sales-orders/detail/{uid}/cancel-confirmation", true),
            (typeof(SalesOrderLifecycleController), "CloseAsync", "POST", "api/sales-orders/detail/{uid}/close", true),
        };
        Assert.AreEqual(10, expected.Length);
        foreach (var item in expected) AssertRoute(item.Controller, item.Method, item.Verb, item.Path, item.Debounce);
        var confirm = typeof(SalesOrderConfirmationController).GetMethod("ConfirmAsync")!;
        Assert.IsTrue(confirm.IsDefined(typeof(AuthorizeAttribute), false));
        Assert.IsFalse(typeof(SalesOrderConfirmationController).IsDefined(typeof(AuthorizeAttribute), false));
    }

    /// <summary>售后十个动作保留统一资源根路径、类级认证和原防重复标记。</summary>
    [TestMethod]
    public void AfterSalesRoutes_KeepOriginalMethodsAndPaths()
    {
        const string root = "api/quality-control/after-sales";
        var expected = new (Type Controller, string Method, string Verb, string Path, bool Debounce)[]
        {
            (typeof(AfterSalesQueryController), "SearchAsync", "GET", root, false),
            (typeof(AfterSalesQueryController), "GetAsync", "GET", root + "/{uid}", false),
            (typeof(AfterSalesQueryController), "LookupAsync", "GET", root + "/lookups/{kind}", false),
            (typeof(AfterSalesMaintenanceController), "CreateAsync", "POST", root, true),
            (typeof(AfterSalesMaintenanceController), "SaveAsync", "PUT", root + "/{uid}", true),
            (typeof(AfterSalesConfirmationController), "ConfirmDemandAsync", "POST", root + "/{uid}/confirm-demand", true),
            (typeof(AfterSalesConfirmationController), "CancelDemandConfirmationAsync", "POST", root + "/{uid}/cancel-demand-confirmation", true),
            (typeof(AfterSalesLifecycleController), "CloseAsync", "POST", root + "/{uid}/close", true),
            (typeof(AfterSalesLifecycleController), "ReopenAsync", "POST", root + "/{uid}/reopen", true),
            (typeof(AfterSalesLifecycleController), "VoidAsync", "POST", root + "/{uid}/void", true),
        };
        Assert.AreEqual(10, expected.Length);
        foreach (var item in expected)
        {
            AssertRoute(item.Controller, item.Method, item.Verb, item.Path, item.Debounce);
            Assert.IsTrue(item.Controller.IsDefined(typeof(AuthorizeAttribute), false));
        }
    }

    /// <summary>拼合类级与动作级模板，忽略原有 guid 路由约束后比较公开 URL。</summary>
    private static void AssertRoute(Type controller, string methodName, string verb, string path, bool debounce)
    {
        var method = controller.GetMethod(methodName)!;
        Assert.IsNotNull(method, $"缺少 {controller.Name}.{methodName}");
        var http = method.GetCustomAttributes(typeof(HttpMethodAttribute), false).Cast<HttpMethodAttribute>().Single();
        Assert.AreEqual(verb, http.HttpMethods.Single());
        var root = controller.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().SingleOrDefault()?.Template;
        var full = string.Join("/", new[] { root, http.Template }.Where(part => !string.IsNullOrEmpty(part))).Replace(":guid", "");
        Assert.AreEqual(path, full, $"{controller.Name}.{methodName}");
        Assert.AreEqual(debounce, method.IsDefined(typeof(PreventDuplicateRequestAttribute), false));
    }
}
