using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Controllers.Products.Attributes;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证标签 Refit 的真实请求路径、变更包装和控制器路由契约，不需要数据库。</summary>
[TestClass]
public sealed class ProductAttributeApiContractTests
{
    /// <summary>三个 API 操作与 MVC 路由一致，父子临时身份及逐项版本可跨 HTTP 序列化。</summary>
    [TestMethod]
    public async Task RefitAndMvc_RoutesAndDefinitionPayloadAgree()
    {
        var category = Guid.NewGuid(); var temporary = Guid.NewGuid();
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://contract.invalid/api/ProductAttribute") };
        var api = RestService.For<IProductAttributeApi>(client);
        Assert.IsTrue((await api.GetDefinitionsAsync()).Success);
        Assert.IsTrue((await api.GetCategoryDefinitionsAsync(category)).Success);
        var request = new SaveProductAttributeDefinitionsRequest();
        request.GroupChanges.CreateItems.Add(new() { RowIndex = 3, Data = new() { ClientUid = temporary, GroupCode = "SIZE", GroupName = "尺寸" } });
        request.OptionChanges.CreateItems.Add(new() { RowIndex = 4, Data = new() { AttributeGroupId = temporary, OptionCode = "8", OptionName = "8寸" } });
        request.GroupChanges.DeleteItems.Add(new() { Uid = Guid.NewGuid(), Version = 7 });
        Assert.IsTrue((await api.BatchSaveAsync(category, request)).Success);
        CollectionAssert.AreEqual(new[] { "/api/ProductAttribute/definitions", $"/api/ProductAttribute/definitions/{category}", $"/api/ProductAttribute/definitions/{category}/batch-save" }, handler.Paths);
        CollectionAssert.AreEqual(new[] { "GET", "GET", "POST" }, handler.Methods);
        var received = JsonConvert.DeserializeObject<SaveProductAttributeDefinitionsRequest>(handler.Body)!;
        Assert.AreEqual(temporary, received.GroupChanges.CreateItems.Single().Data.ClientUid);
        Assert.AreEqual(temporary, received.OptionChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(7, received.GroupChanges.DeleteItems.Single().Version);
        Assert.AreEqual("api/[controller]", typeof(ProductAttributeController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template);
        Assert.AreEqual("definitions", Route<HttpGetAttribute>(nameof(ProductAttributeController.GetDefinitionsAsync)));
        Assert.AreEqual("definitions/{categoryId:guid}", Route<HttpGetAttribute>(nameof(ProductAttributeController.GetCategoryDefinitionsAsync)));
        Assert.AreEqual("definitions/{categoryId:guid}/batch-save", Route<HttpPostAttribute>(nameof(ProductAttributeController.BatchSaveAsync)));
    }

    /// <summary>读取 MVC 实际方法路由，避免仅验证契约自己的声明。</summary>
    private static string? Route<T>(string method) where T : Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute =>
        typeof(ProductAttributeController).GetMethod(method)!.GetCustomAttributes(typeof(T), true).Cast<T>().Single().Template;

    /// <summary>捕获 Refit 产生的 HTTP 请求，同时返回可解析的成功空快照。</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        internal readonly List<string> Paths = [], Methods = [];
        internal string Body = string.Empty;
        /// <summary>记录实际路径和请求体，模拟服务端使用共享包装返回响应。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath); Methods.Add(request.Method.Method);
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync(cancellationToken);
            var value = request.Method == HttpMethod.Post ? (object)ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(new())
                : ServiceResult<ProductAttributeDefinitionsDto>.Ok(new());
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json") };
        }
    }
}
