using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.QualityControl;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.WebApi.Controllers.QualityControl.Categories;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>验证分类专用接口不泄露单条 CRUD，并保护独立读写契约及取消参数。</summary>
[TestClass]
public sealed class AfterSalesQualityCategoryContractTests
{
    /// <summary>读 DTO 继承审计基类；编辑 DTO 只有明确允许的七个字段。</summary>
    [TestMethod]
    public void ReadAndEditContractsAreSeparated()
    {
        Assert.IsTrue(typeof(BaseDto).IsAssignableFrom(typeof(AfterSalesQualityCategoryDto)));
        Assert.IsFalse(typeof(BaseDto).IsAssignableFrom(typeof(AfterSalesQualityCategoryEditDto)));
        CollectionAssert.AreEquivalent(new[]{"Uid","CategoryName","ParentId","FakeName","CategoryCode","SortNo","IsEnabled"},
            typeof(AfterSalesQualityCategoryEditDto).GetProperties().Select(x=>x.Name).ToArray());
        Assert.IsTrue(new AfterSalesQualityCategoryEditDto().IsEnabled);
    }
    /// <summary>Controller 与站点根 Refit 路径一一对应，两个入口都支持取消且写入仅用 Body。</summary>
    [TestMethod]
    public void OnlyExplicitTreeRoutesAreExposed()
    {
        var controller=typeof(AfterSalesQualityCategoryController);var api=typeof(IAfterSalesQualityCategoryApi);
        Assert.AreEqual(typeof(ControllerBase),controller.BaseType);
        Assert.IsNotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.AreEqual("api/quality-control/after-sales/quality-categories",controller.GetCustomAttribute<RouteAttribute>()!.Template);
        CollectionAssert.AreEquivalent(new[]{"GetAllAsync","BatchSaveAsync"},controller.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Select(x=>x.Name).ToArray());
        Assert.AreEqual("all",controller.GetMethod("GetAllAsync")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.AreEqual("batch-save",controller.GetMethod("BatchSaveAsync")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.AreEqual("/api/quality-control/after-sales/quality-categories/all",api.GetMethod("GetAllAsync")!.GetCustomAttribute<GetAttribute>()!.Path);
        Assert.AreEqual("/api/quality-control/after-sales/quality-categories/batch-save",api.GetMethod("BatchSaveAsync")!.GetCustomAttribute<PostAttribute>()!.Path);
        Assert.IsNotNull(api.GetMethod("BatchSaveAsync")!.GetParameters()[0].GetCustomAttribute<BodyAttribute>());
        foreach(var type in new[]{controller,api})foreach(var name in new[]{"GetAllAsync","BatchSaveAsync"})
            Assert.AreEqual(typeof(CancellationToken),type.GetMethod(name)!.GetParameters().Last().ParameterType);
    }
}
