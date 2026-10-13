using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Application.Interfaces.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Controllers.Products.Attributes;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证标签 Refit 的真实请求路径、变更包装和控制器路由契约，不需要数据库。</summary>
[TestClass]
public sealed class ProductAttributeApiContractTests
{
    /// <summary>全局保存与旧兼容端点分别对齐 MVC 路由，临时父身份和关联逐项版本跨 HTTP 保持。</summary>
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
        request.CategoryChanges.CreateItems.Add(new() { RowIndex = 5, Data = new() { CategoryId = category, AttributeGroupId = temporary } });
        request.CategoryChanges.DeleteItems.Add(new() { Uid = Guid.NewGuid(), Version = 2 });
        request.GroupChanges.DeleteItems.Add(new() { Uid = Guid.NewGuid(), Version = 7 });
        Assert.IsTrue((await api.BatchSaveDefinitionsAsync(request)).Success);
        var globalBody = handler.Body;
        Assert.IsTrue((await api.BatchSaveAsync(category, request)).Success);
        CollectionAssert.AreEqual(new[] { "/api/ProductAttribute/definitions", $"/api/ProductAttribute/definitions/{category}", "/api/ProductAttribute/definitions/batch-save", $"/api/ProductAttribute/definitions/{category}/batch-save" }, handler.Paths);
        CollectionAssert.AreEqual(new[] { "GET", "GET", "POST", "POST" }, handler.Methods);
        var received = JsonConvert.DeserializeObject<SaveProductAttributeDefinitionsRequest>(globalBody)!;
        Assert.AreEqual(temporary, received.GroupChanges.CreateItems.Single().Data.ClientUid);
        Assert.AreEqual(temporary, received.OptionChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(7, received.GroupChanges.DeleteItems.Single().Version);
        Assert.AreEqual(temporary, received.CategoryChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(category, received.CategoryChanges.CreateItems.Single().Data.CategoryId);
        Assert.AreEqual(2, received.CategoryChanges.DeleteItems.Single().Version);
        Assert.AreEqual("api/[controller]", typeof(ProductAttributeController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template);
        Assert.AreEqual("definitions", Route<HttpGetAttribute>(nameof(ProductAttributeController.GetDefinitionsAsync)));
        Assert.AreEqual("definitions/{categoryId:guid}", Route<HttpGetAttribute>(nameof(ProductAttributeController.GetCategoryDefinitionsAsync)));
        Assert.AreEqual("definitions/batch-save", Route<HttpPostAttribute>(nameof(ProductAttributeController.BatchSaveDefinitionsAsync)));
        Assert.AreEqual("definitions/{categoryId:guid}/batch-save", Route<HttpPostAttribute>(nameof(ProductAttributeController.BatchSaveAsync)));
    }

    /// <summary>控制器分别转发新全局与旧兼容调用，并完整透传请求取消令牌。</summary>
    [TestMethod]
    public async Task Controller_KeepsGlobalAndLegacySaveSeparate()
    {
        var stub = new DefinitionServiceStub();
        var controller = new ProductAttributeController(stub);
        var request = new SaveProductAttributeDefinitionsRequest();
        using var cancellation = new CancellationTokenSource();
        var categoryId = Guid.NewGuid();
        Assert.IsTrue((await controller.BatchSaveDefinitionsAsync(request, cancellation.Token)).Success);
        Assert.IsFalse((await controller.BatchSaveAsync(categoryId, request, cancellation.Token)).Success);
        Assert.AreSame(request, stub.GlobalRequest);
        Assert.AreEqual(categoryId, stub.LegacyCategoryId);
        Assert.AreEqual(cancellation.Token, stub.Token);
    }

    /// <summary>区分兼容端点与全局写入口，不借助数据库测试控制器转发。</summary>
    private sealed class DefinitionServiceStub : IProductAttributeDefinitionService
    {
        internal SaveProductAttributeDefinitionsRequest? GlobalRequest;
        internal Guid LegacyCategoryId;
        internal CancellationToken Token;
        /// <inheritdoc />
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(new()));
        /// <inheritdoc />
        public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
        { GlobalRequest = request; Token = cancellationToken; return Task.FromResult(ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(new())); }
        /// <inheritdoc />
        public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
        { LegacyCategoryId = categoryId; Token = cancellationToken; return Task.FromResult(ServiceResult<SaveProductAttributeDefinitionsResponse>.Fail("BUSINESS_BLOCKED", "请升级客户端。")); }
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
