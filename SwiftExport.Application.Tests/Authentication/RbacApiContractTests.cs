using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.WebApi.Controllers.Systems;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证 RBAC 管理 Refit 与实际 Controller 的方法、路由和认证边界一致。</summary>
[TestClass]
public sealed class RbacApiContractTests
{
    /// <summary>全部十六个共享管理端点都必须命中受保护的后端 Action。</summary>
    [TestMethod]
    public void RefitRoutes_MatchProtectedManagementActions()
    {
        var routes = new (string ApiMethod, Type Controller, string Action, string Verb, string RelativePath)[]
        {
            ("SearchUsersAsync", typeof(RbacUsersController), "Search", "POST", "page"),
            ("GetUserAsync", typeof(RbacUsersController), "Get", "GET", "{id}"),
            ("UpdateUserAsync", typeof(RbacUsersController), "Update", "PUT", "{id}"),
            ("SetUserStatusAsync", typeof(RbacUsersController), "SetStatus", "PUT", "{id}/status"),
            ("GetUserRolesAsync", typeof(RbacUsersController), "GetRoles", "GET", "{id}/roles"),
            ("ReplaceUserRolesAsync", typeof(RbacUsersController), "ReplaceRoles", "PUT", "{id}/roles"),
            ("GetEffectivePermissionsAsync", typeof(RbacUsersController), "GetEffectivePermissions", "GET", "{id}/effective-permissions"),
            ("SearchRolesAsync", typeof(RbacRolesController), "Search", "POST", "page"),
            ("GetRoleAsync", typeof(RbacRolesController), "Get", "GET", "{id}"),
            ("CreateRoleAsync", typeof(RbacRolesController), "Create", "POST", ""),
            ("UpdateRoleAsync", typeof(RbacRolesController), "Update", "PUT", "{id}"),
            ("SetRoleStatusAsync", typeof(RbacRolesController), "SetStatus", "PUT", "{id}/status"),
            ("DeleteRoleAsync", typeof(RbacRolesController), "Delete", "DELETE", "{id}"),
            ("GetRolePermissionsAsync", typeof(RbacRolesController), "GetPermissions", "GET", "{id}/permissions"),
            ("ReplaceRolePermissionsAsync", typeof(RbacRolesController), "ReplacePermissions", "PUT", "{id}/permissions"),
            ("GetPermissionsAsync", typeof(RbacPermissionsController), "List", "GET", "")
        };
        var apiMethods = typeof(ISystemRbacApi).GetMethods();
        Assert.AreEqual(16, routes.Length);
        Assert.AreEqual(routes.Length, apiMethods.Length);

        foreach (var route in routes)
        {
            Assert.IsNotNull(route.Controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(), route.Controller.Name);
            var prefix = route.Controller.GetCustomAttribute<RouteAttribute>()?.Template;
            Assert.IsFalse(string.IsNullOrWhiteSpace(prefix), route.Controller.Name);
            var action = route.Controller.GetMethod(route.Action);
            Assert.IsNotNull(action, route.Controller.Name + "." + route.Action);
            Assert.IsNull(action.GetCustomAttribute<AllowAnonymousAttribute>(), route.Controller.Name + "." + route.Action);
            var http = action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single();
            Assert.AreEqual(route.Verb, http.HttpMethods.Single(), route.ApiMethod);
            var controllerPath = "/" + prefix!.Trim('/') + "/" + (http.Template ?? "").Trim('/');
            var api = apiMethods.Single(method => method.Name == route.ApiMethod);
            var refit = api.GetCustomAttributes().OfType<Refit.HttpMethodAttribute>().Single();
            Assert.AreEqual(route.Verb, refit.Method.Method, route.ApiMethod);
            Assert.AreEqual(Normalize(controllerPath), Normalize(refit.Path), route.ApiMethod);
        }
    }

    /// <summary>路由比较只归一化后端的 Guid 约束和尾斜线，保留资源及动作语义。</summary>
    private static string Normalize(string path) => path.Replace(":guid", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
}
