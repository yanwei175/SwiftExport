using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.WebApi.Controllers.Products.Catalog;
using SwiftExport.WebApi.Controllers.Customers.CustomerProducts;
using SwiftExport.WebApi.Controllers.Suppliers.SupplierProducts;
using SwiftExport.WebApi.RequestProtection;
using System.Reflection;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>保护职责拆分后原基础 CRUD 与扩展路由及防重复标记。</summary>
[TestClass]
public sealed class MasterDataRouteContractTests
{
    /// <summary>三个拆分资源保留原 BaseController 的四个公开动作。</summary>
    [TestMethod]
    public void SplitControllers_KeepInheritedCrudRoutesAndProtection()
    {
        foreach (var controller in new[] { typeof(ProductController), typeof(CustomerProductController), typeof(SupplierProductController) })
        {
            Assert.AreEqual("api/[controller]", controller.GetCustomAttribute<RouteAttribute>()?.Template);
            AssertRoute(controller, "GetById", "GET", "{id}", false);
            AssertRoute(controller, "Create", "POST", null, true);
            AssertRoute(controller, "Update", "PUT", "{id}", true);
            AssertRoute(controller, "Delete", "DELETE", "{id}", true);
            AssertRoute(controller, "GetPaged", "POST", "page", false);
            AssertRoute(controller, "BatchSave", "POST", "batch-save", true);
        }
        AssertRoute(typeof(SupplierProductController), "SetProductDefaultSupplier", "POST", "set-product-default-supplier", true);
    }

    /// <summary>读取实际动作元数据，不依赖 Controller 文件路径。</summary>
    private static void AssertRoute(Type controller, string action, string method, string? template, bool protectedWrite)
    {
        var member = controller.GetMethod(action);
        Assert.IsNotNull(member, $"{controller.Name}.{action} 缺失。");
        var route = member.GetCustomAttributes<HttpMethodAttribute>().Single();
        CollectionAssert.Contains(route.HttpMethods.ToArray(), method);
        Assert.AreEqual(template, route.Template);
        Assert.AreEqual(protectedWrite, member.IsDefined(typeof(PreventDuplicateRequestAttribute), true));
    }
}
