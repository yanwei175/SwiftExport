using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.WebApi.Controllers.Products.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证物料详情独立端点、可写边界及标签保持协议，不访问数据库。</summary>
[TestClass]
public sealed class ProductDetailApiContractTests
{
    /// <summary>读取与保存的 HTTP 方法及路径一致，详情新端点不改变基础产品契约。</summary>
    [TestMethod]
    public void DetailRoutes_AlignControllerAndRefit()
    {
        Assert.AreEqual("/{id}/detail", ((GetAttribute)Attribute.GetCustomAttribute(typeof(IProductApi).GetMethod("GetDetailAsync")!, typeof(GetAttribute))!).Path);
        Assert.AreEqual("/{id}/detail", ((PostAttribute)Attribute.GetCustomAttribute(typeof(IProductApi).GetMethod("SaveDetailAsync")!, typeof(PostAttribute))!).Path);
        Assert.AreEqual("{id}/detail", ((HttpGetAttribute)Attribute.GetCustomAttribute(typeof(ProductController).GetMethod("GetDetail")!, typeof(HttpGetAttribute))!).Template);
        Assert.AreEqual("{id}/detail", ((HttpPostAttribute)Attribute.GetCustomAttribute(typeof(ProductController).GetMethod("SaveDetail")!, typeof(HttpPostAttribute))!).Template);
    }

    /// <summary>省略和 null 保持选择，明确空集合才清空；写入资料不接受只读来源或 BOM 字段。</summary>
    [TestMethod]
    public void DetailWrite_ExcludesReadOnlyFieldsAndKeepsNullProtocol()
    {
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductDetailRequest>("{}")!.SelectedOptionIds);
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductDetailRequest>("{\"SelectedOptionIds\":null}")!.SelectedOptionIds);
        Assert.HasCount(0, JsonConvert.DeserializeObject<SaveProductDetailRequest>("{\"SelectedOptionIds\":[]}")!.SelectedOptionIds!);
        var fields = typeof(ProductDetailWriteDto).GetProperties().Select(row => row.Name).ToHashSet();
        foreach (var name in new[] { "ProductMasterId", "MasterSku", "ProductAttributeProfileId", "CategoryId", "IsActive", "BomNo", "BomVersion", "PcsOfCarton", "NwOfCarton", "GwOfCarton", "CbmOfCarton", "OwnerType" })
            Assert.IsFalse(fields.Contains(name), $"只读字段 {name} 不能进入物料详情写契约。");
        Assert.IsTrue(fields.Contains("UnitWeight") && fields.Contains("UnitCBM"));
    }

    /// <summary>路径身份冲突直接返回校验失败，不调用任何查询或写入服务。</summary>
    [TestMethod]
    public async Task DetailSave_RejectsMismatchedIdentityBeforeService()
    {
        var controller = new ProductController(null!, null!, null!);
        var result = await controller.SaveDetail(Guid.NewGuid(), new SaveProductDetailRequest { ProductId = Guid.NewGuid() }, default);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("VALIDATION", result.Error.Code);
    }
}
