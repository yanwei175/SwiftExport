using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.SalesOrders.Confirmation;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Application.Interfaces.SalesOrders.Draft;
using SwiftExport.Application.Interfaces.SalesOrders.Lifecycle;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.WebApi.Controllers.SalesOrders.Query;
using SwiftExport.WebApi.Controllers.SalesOrders.Draft;
using SwiftExport.WebApi.Controllers.SalesOrders.Confirmation;
using SwiftExport.WebApi.Controllers.SalesOrders.Lifecycle;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>
/// 验证销售订单 Manager/Detail 的服务职责和 HTTP/Refit 契约保持一致。
/// </summary>
[TestClass]
public sealed class SalesOrderManagerDetailContractTests
{
    /// <summary>
    /// 验证列表与详情查询、草稿、确认和生命周期均由真实窄接口承接。
    /// </summary>
    [TestMethod]
    public void ApplicationServices_ShouldKeepManagerAndDetailResponsibilitiesSeparated()
    {
        CollectionAssert.AreEquivalent(
            new[] { "SearchAsync", "GetCustomerCodesAsync", "GetAsync", "SearchProductsAsync", "SearchCustomersAsync" },
            typeof(ISalesOrderQueryService).GetMethods().Select(method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "SaveAsync", "DeleteDraftAsync" },
            typeof(ISalesOrderDraftService).GetMethods().Select(method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "ConfirmAsync" },
            typeof(ISalesOrderConfirmationService).GetMethods().Select(method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "CancelConfirmationAsync" },
            typeof(ISalesOrderCancellationService).GetMethods().Select(method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "CloseAsync" },
            typeof(ISalesOrderLifecycleService).GetMethods().Select(method => method.Name).ToArray());
    }

    /// <summary>
    /// 验证 Controller 基路由与 Refit 相对路径、HTTP 方法及 Body 参数严格对齐。
    /// </summary>
    [TestMethod]
    public void ControllersAndRefitApis_ShouldUseAlignedManagerAndDetailRoutes()
    {
        Assert.AreEqual("api/sales-orders/manager/search", RouteOf<SalesOrderQueryController>("SearchAsync"));
        Assert.AreEqual("api/sales-orders/manager/customer-codes", RouteOf<SalesOrderQueryController>("GetCustomerCodesAsync"));
        Assert.AreEqual("api/sales-orders/detail/{uid:guid}", RouteOf<SalesOrderQueryController>("GetAsync"));
        Assert.AreEqual("api/sales-orders/detail/products/search", RouteOf<SalesOrderQueryController>("SearchProductsAsync"));
        Assert.AreEqual("api/sales-orders/detail/customers/search", RouteOf<SalesOrderQueryController>("SearchCustomersAsync"));
        Assert.AreEqual("api/sales-orders/detail", typeof(SalesOrderDraftController).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single().Template);
        Assert.AreEqual("api/sales-orders/detail", typeof(SalesOrderConfirmationController).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single().Template);
        Assert.AreEqual("api/sales-orders/detail", typeof(SalesOrderLifecycleController).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single().Template);

        AssertRefitPath<ISalesOrderManagerApi, PostAttribute>("SearchAsync", "/search");
        AssertRefitPath<ISalesOrderManagerApi, GetAttribute>("GetCustomerCodesAsync", "/customer-codes");
        AssertRefitPath<ISalesOrderDetailApi, GetAttribute>("GetAsync", "/{uid}");
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("SearchProductsAsync", "/products/search", true);
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("SearchCustomersAsync", "/customers/search", true);
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("SaveAsync", "/save", true);
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("ConfirmAsync", "/{uid}/confirm", true);
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("CancelConfirmationAsync", "/{uid}/cancel-confirmation", true);
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("CloseAsync", "/{uid}/close", true);
        AssertRefitPath<ISalesOrderDetailApi, PostAttribute>("DeleteDraftAsync", "/{uid}/delete-draft", true);
    }

    /// <summary>
    /// 验证销售订单公开契约、Application 服务和 Controller 不再暴露 Workspace 类型。
    /// </summary>
    [TestMethod]
    public void PublicSalesOrderBoundaries_ShouldNotExposeWorkspaceTypes()
    {
        var types = typeof(ISalesOrderManagerApi).Assembly.GetExportedTypes()
            .Concat(typeof(ISalesOrderQueryService).Assembly.GetExportedTypes())
            .Concat(typeof(SalesOrderQueryController).Assembly.GetExportedTypes())
            .Where(type => type.Name.Contains("SalesOrder", StringComparison.Ordinal))
            .ToArray();

        Assert.IsFalse(types.Any(type => type.Name.Contains("Workspace", StringComparison.Ordinal)));
        Assert.IsFalse(types.Any(type =>
            type.Namespace?.StartsWith(
                "SwiftExport.Contracts.DTOs.SalesOrders.Workspace",
                StringComparison.Ordinal) == true));
    }

    /// <summary>读取查询 Controller 的显式动作路由，确保两个旧根路径未因合并而变化。</summary>
    private static string RouteOf<TController>(string methodName)
        => typeof(TController).GetMethod(methodName)!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single().Template!;

    /// <summary>
    /// 验证指定 Refit 方法具有预期 HTTP 属性，并在要求时声明一个 Body 参数。
    /// </summary>
    private static void AssertRefitPath<TApi, THttpAttribute>(
        string methodName,
        string expectedPath,
        bool requiresBody = false)
        where THttpAttribute : HttpMethodAttribute
    {
        var method = typeof(TApi).GetMethod(methodName);
        Assert.IsNotNull(method, $"找不到 Refit 方法 {typeof(TApi).Name}.{methodName}。");
        var attribute = method.GetCustomAttributes(typeof(THttpAttribute), false)
            .Cast<THttpAttribute>()
            .Single();
        Assert.AreEqual(expectedPath, attribute.Path);

        if (requiresBody)
        {
            Assert.AreEqual(
                1,
                method.GetParameters().Count(parameter =>
                    parameter.GetCustomAttributes(typeof(BodyAttribute), false).Length == 1),
                $"{typeof(TApi).Name}.{methodName} 必须且只能包含一个 Body 参数。");
        }
    }
}
