using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.WebApi.Controllers.Inventory.Outbounds;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>确认拆分后的四个 Controller 仍与原 Refit 接口保持十个端点的路由和类型契约。</summary>
[TestClass]
public sealed class OutboundRouteContractTests
{
    /// <summary>逐端点比较 HTTP 方法、路径、请求参数类型和返回包装类型。</summary>
    [TestMethod]
    public void Controllers_MatchExistingRefitContract()
    {
        var controllers = new[]
        {
            typeof(OutboundQueryController), typeof(OutboundDraftController),
            typeof(OutboundPostingController), typeof(OutboundReversalController)
        };
        var actions = controllers.SelectMany(type => type.GetMethods().Where(method => method.DeclaringType == type))
            .ToDictionary(method => method.Name);
        var api = typeof(IOutboundManagerApi).GetMethods();
        Assert.HasCount(10, api);
        Assert.HasCount(10, actions);
        foreach (var method in api)
        {
            var action = actions[method.Name];
            Assert.AreEqual(method.ReturnType, action.ReturnType, method.Name);
            CollectionAssert.AreEqual(method.GetParameters().Select(x => x.ParameterType).ToArray(),
                action.GetParameters().Select(x => x.ParameterType).ToArray(), method.Name);
            var expected = method.GetCustomAttributes(false).OfType<Refit.HttpMethodAttribute>().Single();
            var actual = action.GetCustomAttributes(false).OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single();
            Assert.AreEqual(expected.Method.Method, actual.HttpMethods.Single(), method.Name);
            Assert.AreEqual(expected.Path.TrimStart('/'), actual.Template!.Replace(":guid", ""), method.Name);
        }
        foreach (var type in controllers)
            Assert.AreEqual("api/inventory/outbounds", type.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Single().Template);
    }
}
