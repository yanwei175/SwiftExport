using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.WebApi.Controllers.Products.ProductMasters;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>核对独立下发的 Controller 与 Refit 共同使用五个 POST 路由及窄请求响应。</summary>
[TestClass]
public sealed class ProductMasterSyncApiContractTests
{
    /// <summary>逐个验证路由、HTTP 方法和同一请求响应契约，防止菜单调用到错误下发范围。</summary>
    [TestMethod]
    public void SyncActions_MatchRefitRoutesAndContracts()
    {
        foreach (var name in new[]
        {
            nameof(IProductMasterApi.SyncDescriptionsAsync),
            nameof(IProductMasterApi.SyncFeaturesAsync),
            nameof(IProductMasterApi.SyncSupplierAsync),
            nameof(IProductMasterApi.SyncWeightVolumeAsync),
            nameof(IProductMasterApi.SyncAllAsync)
        })
        {
            var api = typeof(IProductMasterApi).GetMethod(name)!;
            var controller = typeof(ProductMasterController).GetMethod(name)!;
            var refit = api.GetCustomAttribute<PostAttribute>()!;
            var action = controller.GetCustomAttribute<HttpPostAttribute>()!;
            Assert.AreEqual(refit.Path, "/" + action.Template, name);
            Assert.AreEqual(typeof(SyncProductMasterDataRequest), api.GetParameters()[0].ParameterType, name);
            Assert.AreEqual(typeof(SyncProductMasterDataRequest), controller.GetParameters()[0].ParameterType, name);
            StringAssert.Contains(api.ReturnType.FullName!, nameof(SyncProductMasterDataResponse), name);
            Assert.AreEqual(api.ReturnType, controller.ReturnType, name);
        }
    }
}
