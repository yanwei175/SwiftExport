using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Bootstrapper;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>以真实 Refit 序列化验证独立明细接口，不访问网络。</summary>
[TestClass]
public sealed class ProductMasterNeutralContractTests
{
    /// <summary>批量保存路由、请求体和返回详情沿用现有契约，并在客户端组合根注册。</summary>
    [TestMethod]
    public async Task Refit_ShouldSendNeutralBatchAndDeserializeDetail()
    {
        var services = new ServiceCollection();
        services.AddSwiftExportApis();
        Assert.IsTrue(services.Any(item => item.ServiceType == typeof(IProductNeutralApi)));
        using var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/api/ProductNeutral") };
        var api = RestService.For<IProductNeutralApi>(client);
        var parent = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var response = await api.BatchSaveAsync(parent, new BatchSaveRequestBase<ProductNeutralWriteDto>
        {
            CreateItems = [new() { ClientKey = "new-row", Data = new() { ProductCode = "N", IsMainImage = true } }],
            DeleteItems = [new() { Uid = Guid.NewGuid(), Version = 4 }]
        }, cts.Token);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual($"/api/ProductNeutral/{parent}/batch-save", handler.Path);
        Assert.IsTrue(handler.TokenCanCancel);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual("N", json.RootElement.GetProperty("createItems")[0].GetProperty("data").GetProperty("productCode").GetString());
        Assert.AreEqual(4, json.RootElement.GetProperty("deleteItems")[0].GetProperty("version").GetInt32());
        Assert.IsFalse(json.RootElement.TryGetProperty("master", out _));
        Assert.IsTrue(response.Success);
        Assert.AreEqual(5, response.Data.Version);
        Assert.AreEqual("SAVED", response.Data.Neutrals.Single().Data.ProductCode);
    }

    /// <summary>截获完整 HTTP 请求并返回确定的详情 JSON。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public bool TokenCanCancel { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            TokenCanCancel = cancellationToken.CanBeCanceled;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{"version":5,"neutrals":[{"version":2,"data":{"productCode":"SAVED"}}]}}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
