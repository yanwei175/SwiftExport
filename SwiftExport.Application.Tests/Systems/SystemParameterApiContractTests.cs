using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.WebApi.Controllers.Systems.SystemParameters;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>系统参数独立契约与管理员权限目录检查，不依赖业务 DTO 或数据库。</summary>
[TestClass]
public sealed class SystemParameterApiContractTests
{
    /// <summary>Controller 和相对 Refit 路由、动词、返回类型与 Body 对齐，不暴露增删入口。</summary>
    [TestMethod]
    public void RoutesAndCatalog_ShouldExposeOnlyReadAndVersionedUpdate()
    {
        var controller = typeof(SystemParameterController);
        Assert.IsNotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.AreEqual("api/system-parameters", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        var methods = typeof(ISystemParameterApi).GetMethods();
        Assert.HasCount(2, methods);
        foreach (var method in methods)
        {
            var action = controller.GetMethod(method.Name)!;
            var refit = method.GetCustomAttributes().OfType<Refit.HttpMethodAttribute>().Single();
            var http = action.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single();
            Assert.AreEqual("/" + (http.Template ?? string.Empty), refit.Path);
            Assert.AreEqual(refit.Method.Method, http.HttpMethods.Single());
            Assert.AreEqual(method.ReturnType, action.ReturnType);
            Assert.AreEqual(typeof(CancellationToken), action.GetParameters().Last().ParameterType);
            foreach (var body in method.GetParameters().Where(parameter => parameter.GetCustomAttribute<BodyAttribute>() is not null))
                Assert.IsNotNull(action.GetParameters().Single(parameter => parameter.Name == body.Name).GetCustomAttribute<FromBodyAttribute>());
        }
        Assert.IsFalse(PermissionCatalog.All.Single(definition => definition.PermissionKey == "System.Parameters.Manage").IsAssignable);
    }
}
