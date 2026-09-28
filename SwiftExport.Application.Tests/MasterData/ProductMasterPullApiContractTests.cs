using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.WebApi.Controllers.Products.Catalog;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>核对产品页五个拉取入口的 HTTP 契约与服务端固定字段组。</summary>
[TestClass]
public sealed class ProductMasterPullApiContractTests
{
    /// <summary>五条 POST 路由与 Refit 使用同一请求响应类型，避免产品页调用错字段组。</summary>
    [TestMethod]
    public void PullActions_MatchRefitRoutesAndContracts()
    {
        foreach (var name in new[]
        {
            nameof(IProductApi.PullMasterDescriptionsAsync),
            nameof(IProductApi.PullMasterFeaturesAsync),
            nameof(IProductApi.PullMasterSupplierAsync),
            nameof(IProductApi.PullMasterWeightVolumeAsync),
            nameof(IProductApi.PullMasterAllAsync)
        })
        {
            var api = typeof(IProductApi).GetMethod(name)!;
            var controller = typeof(ProductController).GetMethod(name)!;
            Assert.AreEqual(api.GetCustomAttribute<PostAttribute>()!.Path,
                "/" + controller.GetCustomAttribute<HttpPostAttribute>()!.Template, name);
            Assert.AreEqual(typeof(PullProductMasterDataRequest), api.GetParameters()[0].ParameterType, name);
            Assert.AreEqual(typeof(PullProductMasterDataRequest), controller.GetParameters()[0].ParameterType, name);
            Assert.AreEqual(api.ReturnType, controller.ReturnType, name);
            StringAssert.Contains(api.ReturnType.FullName!, nameof(PullProductMasterDataResponse), name);
        }
    }

    /// <summary>每条 Controller Action 指定自己的字段组，客户端请求不包含字段组或来源值。</summary>
    [TestMethod]
    public async Task PullActions_FixEachFieldGroup()
    {
        var captured = new List<ProductMasterSyncFields>();
        var service = DispatchProxy.Create<IProductMasterMaintenanceService, CaptureProxy>();
        ((CaptureProxy)(object)service).Capture = fields => captured.Add(fields);
        var controller = new ProductController(null!, null!, service);
        var request = new PullProductMasterDataRequest { ProductIds = [Guid.NewGuid()] };
        Func<Task>[] actions =
        {
            () => controller.PullMasterDescriptionsAsync(request, default),
            () => controller.PullMasterFeaturesAsync(request, default),
            () => controller.PullMasterSupplierAsync(request, default),
            () => controller.PullMasterWeightVolumeAsync(request, default),
            () => controller.PullMasterAllAsync(request, default)
        };
        foreach (var action in actions) await action();
        CollectionAssert.AreEqual(new[]
        {
            ProductMasterSyncFields.Descriptions, ProductMasterSyncFields.Features,
            ProductMasterSyncFields.Supplier, ProductMasterSyncFields.WeightVolume, ProductMasterSyncFields.All
        }, captured);
        CollectionAssert.AreEqual(new[] { nameof(PullProductMasterDataRequest.ProductIds) },
            typeof(PullProductMasterDataRequest).GetProperties().Select(property => property.Name).ToArray());
    }

    /// <summary>仅记录 Controller 向维护服务传入的字段组，不执行数据库写入。</summary>
    public class CaptureProxy : DispatchProxy
    {
        public Action<ProductMasterSyncFields>? Capture { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.AreEqual(nameof(IProductMasterMaintenanceService.PullProductsAsync), targetMethod!.Name);
            Capture!((ProductMasterSyncFields)args![1]!);
            return Task.FromResult(ServiceResult<PullProductMasterDataResponse>.Ok(new()));
        }
    }
}
