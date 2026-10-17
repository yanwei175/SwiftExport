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

    /// <summary>标签保持 null 协议；需要 BOM 意图可写，模板、BOM 编号和版本整数不进入写契约。</summary>
    [TestMethod]
    public void DetailWrite_ExcludesReadOnlyFieldsAndKeepsNullProtocol()
    {
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductDetailRequest>("{}")!.SelectedOptionIds);
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductDetailRequest>("{\"SelectedOptionIds\":null}")!.SelectedOptionIds);
        Assert.HasCount(0, JsonConvert.DeserializeObject<SaveProductDetailRequest>("{\"SelectedOptionIds\":[]}")!.SelectedOptionIds!);
        var fields = typeof(ProductDetailWriteDto).GetProperties().Select(row => row.Name).ToHashSet();
        foreach (var name in new[] { "MasterSku", "ProductNeutralId", "ProductAttributeProfileId", "CategoryId", "IsActive", "BomNo", "BomVersion", "PcsOfCarton", "NwOfCarton", "GwOfCarton", "CbmOfCarton", "OwnerType" })
            Assert.IsFalse(fields.Contains(name), $"只读字段 {name} 不能进入物料详情写契约。");
        Assert.IsTrue(fields.Contains("UnitWeight") && fields.Contains("UnitCBM"));
        Assert.AreEqual(typeof(Guid?), typeof(ProductDetailWriteDto).GetProperty("ProductMasterId")!.PropertyType);
        Assert.AreEqual(typeof(string), typeof(ProductDetailWriteDto).GetProperty("LinkSku")!.PropertyType);
        Assert.AreEqual(typeof(bool?), typeof(ProductDetailWriteDto).GetProperty("RequiresBom")!.PropertyType);
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductDetailRequest>("{}")!.Data.RequiresBom,
            "旧客户端省略意图时不能默认为取消需要 BOM。");
        Assert.AreEqual(true, JsonConvert.DeserializeObject<SaveProductDetailRequest>("{\"Data\":{\"RequiresBom\":true}}")!.Data.RequiresBom);
        Assert.AreEqual(false, JsonConvert.DeserializeObject<SaveProductDetailRequest>("{\"Data\":{\"RequiresBom\":false}}")!.Data.RequiresBom);
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
