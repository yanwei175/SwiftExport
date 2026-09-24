using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Orders.SalesOrders.Confirmation;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.WebApi.Controllers.Orders;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>
/// 验证销售订单 Manager/Detail 的服务职责和 HTTP/Refit 契约保持一致。
/// </summary>
[TestClass]
public sealed class SalesOrderManagerDetailContractTests
{
    /// <summary>
    /// 验证 Manager 仅包含列表查询，Detail 独占详情读取和写命令。
    /// </summary>
    [TestMethod]
    public void ApplicationServices_ShouldKeepManagerAndDetailResponsibilitiesSeparated()
    {
        CollectionAssert.AreEquivalent(
            new[] { "SearchAsync", "GetCustomerCodesAsync" },
            typeof(ISalesOrderManagerService).GetMethods().Select(method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[]
            {
                "GetAsync",
                "SearchProductsAsync",
                "SearchCustomersAsync",
                "SaveAsync",
                "CancelConfirmationAsync",
                "CloseAsync",
                "DeleteDraftAsync"
            },
            typeof(ISalesOrderDetailService).GetMethods().Select(method => method.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "ConfirmAsync" },
            typeof(ISalesOrderConfirmationService).GetMethods().Select(method => method.Name).ToArray());
    }

    /// <summary>
    /// 验证 Controller 基路由与 Refit 相对路径、HTTP 方法及 Body 参数严格对齐。
    /// </summary>
    [TestMethod]
    public void ControllersAndRefitApis_ShouldUseAlignedManagerAndDetailRoutes()
    {
        Assert.AreEqual(
            "api/sales-orders/manager",
            typeof(SalesOrderManagerController).GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Single().Template);
        Assert.AreEqual(
            "api/sales-orders/detail",
            typeof(SalesOrderDetailController).GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Single().Template);

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
            .Concat(typeof(ISalesOrderManagerService).Assembly.GetExportedTypes())
            .Concat(typeof(SalesOrderManagerController).Assembly.GetExportedTypes())
            .Where(type => type.Name.Contains("SalesOrder", StringComparison.Ordinal))
            .ToArray();

        Assert.IsFalse(types.Any(type => type.Name.Contains("Workspace", StringComparison.Ordinal)));
        Assert.IsFalse(types.Any(type =>
            type.Namespace?.StartsWith(
                "SwiftExport.Contracts.DTOs.SalesOrders.Workspace",
                StringComparison.Ordinal) == true));
    }

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
