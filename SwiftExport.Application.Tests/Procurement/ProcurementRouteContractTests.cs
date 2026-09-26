using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Query;
using System.Reflection;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>逐项核对采购四组 Refit API 与拆分后的 Controller 路由、HTTP 方法和返回类型。</summary>
[TestClass]
public sealed class ProcurementRouteContractTests
{
    /// <summary>确保 21 项公开用例在服务端均有唯一对应入口，且客户端协议未丢失参数或返回类型。</summary>
    [TestMethod]
    public void AllProcurementRoutesMatchRefitContracts()
    {
        var prefixes = new Dictionary<Type, string>
        {
            [typeof(IProcurementOrderApi)] = "api/procurement/orders",
            [typeof(IPurchaseOrderManagerApi)] = "api/procurement/order-manager",
            [typeof(IPurchaseDraftApi)] = "api/procurement/purchase-drafts",
            [typeof(IPurchaseOrderProgressApi)] = "api/procurement/purchase-order-progress"
        };
        var controllers = typeof(ProcurementOrderQueryController).Assembly.GetTypes()
            .Where(type => type.IsClass && type.Namespace?.StartsWith(
                "SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders", StringComparison.Ordinal) == true)
            .ToArray();
        var actual = controllers.SelectMany(type =>
        {
            var prefix = type.GetCustomAttribute<RouteAttribute>()?.Template
                ?? throw new AssertFailedException("采购 Controller 缺少根路由：" + type.Name);
            Assert.IsNotNull(type.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(method => (prefix, method, route: method.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().SingleOrDefault()))
                .Where(item => item.route is not null)
                .Select(item => Key(item.prefix, item.route!.HttpMethods.Single(), item.route.Template, item.method.Name,
                    item.method.ReturnType, item.method.GetParameters()));
        }).ToArray();
        var expected = prefixes.SelectMany(pair => pair.Key.GetMethods().Select(method =>
        {
            var route = method.GetCustomAttributesData().Single(attribute =>
                attribute.AttributeType.Namespace == typeof(GetAttribute).Namespace &&
                attribute.AttributeType.Name is "GetAttribute" or "PostAttribute" or "PutAttribute");
            var http = route.AttributeType.Name.Replace("Attribute", "").ToUpperInvariant();
            var suffix = (string?)route.ConstructorArguments[0].Value;
            return Key(pair.Value, http, suffix, method.Name, method.ReturnType, method.GetParameters());
        })).ToArray();
        Assert.AreEqual(21, expected.Length);
        CollectionAssert.AreEquivalent(expected, actual);
    }

    private static string Key(string prefix, string http, string? suffix, string name,
        Type result, ParameterInfo[] parameters)
    {
        var path = (prefix.TrimEnd('/') + "/" + (suffix ?? "").TrimStart('/')).TrimEnd('/');
        path = path.Replace(":guid}", "}", StringComparison.Ordinal);
        var responseShape = result.FullName?.Replace("System.Collections.Generic.IReadOnlyList\u00601",
            "System.Collections.Generic.List\u00601", StringComparison.Ordinal);
        return string.Join("|", http, path, name, responseShape,
            string.Join(",", parameters.Select(parameter => parameter.ParameterType.FullName)));
    }
}
