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

/// <summary>验证模板标签 Refit 的实际请求路径、变更包装和 MVC 路由，不需要数据库。</summary>
[TestClass]
public sealed class ProductAttributeApiContractTests
{
    /// <summary>模板读取及全局保存对齐路由，临时父身份、关联版本和契约版本跨 HTTP 保持。</summary>
    [TestMethod]
    public async Task RefitAndMvc_RoutesAndDefinitionPayloadAgree()
    {
        var profile = Guid.NewGuid(); var temporary = Guid.NewGuid();
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://contract.invalid/api/ProductAttribute") };
        var api = RestService.For<IProductAttributeApi>(client);
        Assert.IsTrue((await api.GetDefinitionsAsync()).Success);
        Assert.IsTrue((await api.GetProfileDefinitionsAsync(profile)).Success);
        var request = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        request.GroupChanges.CreateItems.Add(new() { RowIndex = 3, Data = new() { ClientUid = temporary, GroupCode = "SIZE", GroupName = "尺寸" } });
        request.OptionChanges.CreateItems.Add(new() { RowIndex = 4, Data = new() { AttributeGroupId = temporary, OptionCode = "8", OptionName = "8寸" } });
        request.ProfileChanges.CreateItems.Add(new() { RowIndex = 5, Data = new() { ProductAttributeProfileId = profile, AttributeGroupId = temporary } });
        request.ProfileChanges.DeleteItems.Add(new() { Uid = Guid.NewGuid(), Version = 2 });
        request.GroupChanges.DeleteItems.Add(new() { Uid = Guid.NewGuid(), Version = 7 });
        Assert.IsTrue((await api.BatchSaveDefinitionsAsync(request)).Success);
        CollectionAssert.AreEqual(new[] { "/api/ProductAttribute/definitions", $"/api/ProductAttribute/definitions/profiles/{profile}", "/api/ProductAttribute/definitions/batch-save" }, handler.Paths);
        CollectionAssert.AreEqual(new[] { "GET", "GET", "POST" }, handler.Methods);
        var received = JsonConvert.DeserializeObject<SaveProductAttributeDefinitionsRequest>(handler.Body)!;
        Assert.AreEqual(2, received.ContractVersion);
        Assert.AreEqual(temporary, received.GroupChanges.CreateItems.Single().Data.ClientUid);
        Assert.AreEqual(temporary, received.OptionChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(7, received.GroupChanges.DeleteItems.Single().Version);
        Assert.AreEqual(temporary, received.ProfileChanges.CreateItems.Single().Data.AttributeGroupId);
        Assert.AreEqual(profile, received.ProfileChanges.CreateItems.Single().Data.ProductAttributeProfileId);
        Assert.AreEqual(2, received.ProfileChanges.DeleteItems.Single().Version);
        Assert.AreEqual("api/[controller]", typeof(ProductAttributeController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template);
        Assert.AreEqual("definitions", Route<HttpGetAttribute>(nameof(ProductAttributeController.GetDefinitionsAsync)));
        Assert.AreEqual("definitions/profiles/{profileId:guid}", Route<HttpGetAttribute>(nameof(ProductAttributeController.GetProfileDefinitionsAsync)));
        Assert.AreEqual("definitions/batch-save", Route<HttpPostAttribute>(nameof(ProductAttributeController.BatchSaveDefinitionsAsync)));
        Assert.IsNull(typeof(ProductAttributeController).GetMethod("GetCategoryDefinitionsAsync"));
        Assert.IsNull(typeof(ProductAttributeController).GetMethod("BatchSaveAsync"));
        Assert.IsNull(typeof(IProductAttributeApi).GetMethod("GetCategoryDefinitionsAsync"));
    }

    /// <summary>控制器完整转发模板身份、全局请求及取消令牌，不保留分类写入兼容入口。</summary>
    [TestMethod]
    public async Task Controller_ForwardsProfileAndGlobalSave()
    {
        var stub = new DefinitionServiceStub();
        var controller = new ProductAttributeController(stub);
        var request = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        using var cancellation = new CancellationTokenSource();
        var profile = Guid.NewGuid();
        Assert.IsTrue((await controller.GetProfileDefinitionsAsync(profile, cancellation.Token)).Success);
        Assert.AreEqual(profile, stub.ProfileId);
        Assert.IsTrue((await controller.BatchSaveDefinitionsAsync(request, cancellation.Token)).Success);
        Assert.AreSame(request, stub.Request);
        Assert.AreEqual(cancellation.Token, stub.Token);
    }

    /// <summary>只模拟当前定义用例，记录控制器输入以验证转发。</summary>
    private sealed class DefinitionServiceStub : IProductAttributeDefinitionService
    {
        internal SaveProductAttributeDefinitionsRequest? Request;
        internal Guid? ProfileId;
        internal CancellationToken Token;
        /// <inheritdoc />
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? profileId, CancellationToken cancellationToken = default)
        { ProfileId = profileId; Token = cancellationToken; return Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(new())); }
        /// <inheritdoc />
        public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default)
        { Request = request; Token = cancellationToken; return Task.FromResult(ServiceResult<SaveProductAttributeDefinitionsResponse>.Ok(new())); }
    }

    /// <summary>读取 MVC 实际方法路由，避免只验证契约自己的声明。</summary>
    private static string? Route<T>(string method) where T : Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute =>
        typeof(ProductAttributeController).GetMethod(method)!.GetCustomAttributes(typeof(T), true).Cast<T>().Single().Template;

    /// <summary>捕获 Refit 实际 HTTP 请求，并返回可解析的成功空快照。</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        internal readonly List<string> Paths = [], Methods = [];
        internal string Body = string.Empty;
        /// <summary>记录路径、方法和请求体，模拟服务端共享包装响应。</summary>
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
