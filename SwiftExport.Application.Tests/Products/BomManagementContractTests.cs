using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.BomManagement;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.BomManagement;
using SwiftExport.WebApi.Authentication;
using SwiftExport.WebApi.Controllers.Products;
using SwiftExport.WebApi.Controllers.Products.Catalog;

namespace SwiftExport.Application.Tests.Products;

/// <summary>验证管理页专用契约、真实端点权限过滤和有界查询输入，不连接数据库。</summary>
[TestClass]
public sealed class BomManagementContractTests
{
    /// <summary>前后端路由一致，绑定请求只有三个窄字段，读取链有 CancellationToken。</summary>
    [TestMethod]
    public void ApiContracts_MatchControllersAndKeepNarrowWrite()
    {
        AssertRoute<IBomApi, BomController>("SearchProductsAsync", "SearchProducts", "/products/page", "products/page", true);
        AssertRoute<IBomApi, BomController>("GetNumberInfoAsync", "GetNumberInfo", "/number-info", "number-info", false);
        AssertRoute<IProductBomBindingApi, ProductController>("BindAsync", "BindBom", "/{id}/bom-binding", "{id}/bom-binding", true);
        CollectionAssert.AreEquivalent(new[] { "ExpectedVersion", "BomNo", "BindExisting" }, typeof(ProductBomBindingRequest).GetProperties().Select(property => property.Name).ToArray());
        foreach (var method in new[] { "SearchProductsAsync", "GetNumberInfoAsync", "GetBomByBomNo", "GetBomTreeAsync", "ValidateBomAsync" })
            Assert.AreEqual(typeof(CancellationToken), typeof(IBomApi).GetMethod(method)!.GetParameters().Last().ParameterType);
        Assert.AreEqual(typeof(CancellationToken), typeof(IProductApi).GetMethod("SearchLookupAsync")!.GetParameters().Last().ParameterType);
    }

    /// <summary>新增读取端点沿用 BOM 查看权限，绑定端点在 HTTP 边界要求产品编辑。</summary>
    [TestMethod] [DataRow("SearchProducts", false)] [DataRow("GetNumberInfo", false)] [DataRow("BindBom", true)]
    public async Task EndpointPermissions_DenyMissingAndAllowRequiredKey(string action, bool binding)
    {
        var controller = binding ? typeof(ProductController) : typeof(BomController);
        var required = binding ? PermissionKeys.ProductsProductEdit : PermissionKeys.ProductsBomView;
        foreach (var granted in new[] { false, true })
        {
            var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controller.GetTypeInfo(), MethodInfo = controller.GetMethod(action)! };
            var context = new ActionExecutingContext(new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor), [], new Dictionary<string, object?>(), new object());
            var user = Proxy<ICurrentUser>((method, _) => method.Name == "get_UserId" ? (Guid?)Guid.NewGuid() : method.Name == "get_IsAuthenticated" ? true : "测试");
            var evaluator = Proxy<IPermissionEvaluationService>((_, args) => { Assert.AreEqual(required, args![1]); return Task.FromResult(granted); });
            var continued = false;
            await new EndpointPermissionFilter(user, evaluator).OnActionExecutionAsync(context, () =>
            { continued = true; return Task.FromResult(new ActionExecutedContext(context, [], new object())); });
            Assert.AreEqual(granted, continued);
            if (!granted) Assert.AreEqual(ServiceErrorCodes.Forbidden, ((ServiceResult<object>)((ObjectResult)context.Result!).Value!).Error.Code);
        }
    }

    /// <summary>超大分页、偏移溢出、过长输入在访问仓储前拒绝；有效请求保留取消令牌。</summary>
    [TestMethod]
    public async Task QueryValidation_BoundsInputAndForwardsCancellation()
    {
        var calls = 0; using var cts = new CancellationTokenSource();
        var repository = Proxy<IBomManagementQueryRepository>((_, args) =>
        { calls++; Assert.AreEqual(cts.Token, args![1]); return Task.FromResult(new PagedResponseBase<BomProductListItemDto>()); });
        var service = new BomManagementQueryService(repository, NullLogger<BomManagementQueryService>.Instance);
        foreach (var request in new BomProductSearchRequest[]
        { new() { PageIndex = 0 }, new() { PageSize = 201 }, new() { PageIndex = int.MaxValue, PageSize = 200 }, new() { Keyword = new string('x', 201) }, new() { ColorIds = Enumerable.Repeat(Guid.NewGuid(), 301).ToList() } })
        {
            Assert.IsFalse((await service.SearchProductsAsync(request, cts.Token)).Success);
        }
        Assert.AreEqual(0, calls);
        Assert.IsTrue((await service.SearchProductsAsync(new(), cts.Token)).Success); Assert.AreEqual(1, calls);
        Assert.IsFalse((await service.GetNumberInfoAsync(" ", cts.Token)).Success); Assert.AreEqual(1, calls);
    }

    private static void AssertRoute<TApi, TController>(string apiMethod, string action, string apiPath, string controllerPath, bool post)
    {
        var api = typeof(TApi).GetMethod(apiMethod)!; var method = typeof(TController).GetMethod(action)!;
        Assert.AreEqual(apiPath, post ? api.GetCustomAttribute<PostAttribute>()!.Path : api.GetCustomAttribute<GetAttribute>()!.Path);
        Assert.AreEqual(controllerPath, post ? method.GetCustomAttribute<HttpPostAttribute>()!.Template : method.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> action) where T : class
    { var proxy = DispatchProxy.Create<T, BomManagementBindingTests.TestProxy>(); ((BomManagementBindingTests.TestProxy)(object)proxy).Action = action; return proxy; }
}
