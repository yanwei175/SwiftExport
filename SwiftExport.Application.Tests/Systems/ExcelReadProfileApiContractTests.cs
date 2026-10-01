using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.WebApi.Controllers.Systems.ExcelReadProfiles;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>核对地址配置的 Refit 与 Controller HTTP 契约，防止客户端调用未接线的路径或参数。</summary>
[TestClass]
public sealed class ExcelReadProfileApiContractTests
{
    /// <summary>五个 API 路由、动词、返回类型、Body、Query 和取消令牌保持一致，控制器要求认证。</summary>
    [TestMethod]
    public void RoutesAndParameters_MatchController()
    {
        var controller = typeof(ExcelReadProfileController);
        Assert.IsNotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        var contractMethods = typeof(IExcelReadProfileApi).GetMethods();
        Assert.HasCount(5, contractMethods);
        foreach (var contract in contractMethods)
        {
            var action = controller.GetMethod(contract.Name)!;
            Assert.IsNotNull(action);
            var refit = contract.GetCustomAttributes().OfType<Refit.HttpMethodAttribute>().Single();
            var http = action.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single();
            var expectedPath = "/" + root + (http.Template is null ? "" : "/" + http.Template.Replace(":guid", ""));
            Assert.AreEqual(expectedPath, refit.Path, contract.Name);
            Assert.AreEqual(refit.Method.Method, http.HttpMethods.Single(), contract.Name);
            Assert.AreEqual(contract.ReturnType, action.ReturnType, contract.Name);
            Assert.AreEqual(typeof(CancellationToken), contract.GetParameters().Last().ParameterType);
            Assert.AreEqual(typeof(CancellationToken), action.GetParameters().Last().ParameterType);
            foreach (var body in contract.GetParameters().Where(parameter => parameter.GetCustomAttribute<BodyAttribute>() is not null))
                Assert.IsNotNull(action.GetParameters().Single(parameter => parameter.Name == body.Name).GetCustomAttribute<FromBodyAttribute>());
            foreach (var query in contract.GetParameters().Where(parameter => parameter.GetCustomAttribute<QueryAttribute>() is not null))
                Assert.IsNotNull(action.GetParameters().Single(parameter => parameter.Name == query.Name).GetCustomAttribute<FromQueryAttribute>());
        }
    }
}
