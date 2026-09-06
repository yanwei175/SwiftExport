using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Services.MasterData;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>不连接数据库的输入守卫、窄字段及真实 Refit 序列化检查；SQL 行为由专用集成测试验证。</summary>
[TestClass]
public sealed class ProcurementMasterDataQueryTests
{
    /// <summary>未认证、空用户及缺失用户都在仓储访问前被拒绝。</summary>
    [TestMethod]
    public async Task MissingIdentityCannotQuery()
    {
        foreach (var user in new[] { new TestUser(false, Guid.NewGuid()), new TestUser(true, null), new TestUser(true, Guid.Empty) })
        {
            var service = new ProcurementMasterDataQueryService(null!, new UnreachableRepository(), user);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchCustomersAsync(new()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchSuppliersAsync(new()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchProductsAsync(new()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchCustomerHistoryFiltersAsync(new()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SearchSupplierHistoryFiltersAsync(new()));
        }
    }

    /// <summary>非法页码、大小、偏移及超长关键词不能访问数据库；不会静默截断输入。</summary>
    [TestMethod]
    public async Task InvalidQueryCannotReachRepository()
    {
        var service = new ProcurementMasterDataQueryService(null!, new UnreachableRepository(), new TestUser(true, Guid.NewGuid()));
        PagedRequestBase[] requests = [
            null!, new() { PageIndex = 0 }, new() { PageSize = 0 }, new() { PageSize = 101 },
            new() { PageIndex = int.MaxValue, PageSize = 100 }, new() { Keyword = new string('中', 101) }
        ];
        foreach (var request in requests)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.SearchCustomersAsync(request));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SearchSuppliersAsync(request));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SearchProductsAsync(request is null ? null! : new ProcurementProductSearchRequest
            { Keyword = request.Keyword, PageIndex = request.PageIndex, PageSize = request.PageSize }));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SearchCustomerHistoryFiltersAsync(request));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SearchSupplierHistoryFiltersAsync(request));
        }
    }

    /// <summary>已取消请求在任何权限或数据库工作前结束，不返回伪成功空列表。</summary>
    [TestMethod]
    public async Task CancellationIsNotAnEmptySuccess()
    {
        var service = new ProcurementMasterDataQueryService(null!, new UnreachableRepository(), new TestUser(true, Guid.NewGuid()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchCustomersAsync(new(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchSuppliersAsync(new(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchProductsAsync(new(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchCustomerHistoryFiltersAsync(new(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchSupplierHistoryFiltersAsync(new(), cancellation.Token));
    }

    /// <summary>通过实际 Refit 客户端验证五条路由、POST、分页请求和响应，而非仅匹配源码字符串。</summary>
    [TestMethod]
    public async Task RefitSendsNarrowPagedQueries()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var api = RestService.For<IProcurementMasterDataQueryApi>(client);
        var request = new PagedRequestBase { Keyword = "阀门", PageIndex = 2, PageSize = 5 };
        Assert.IsTrue((await api.SearchCustomersAsync(request)).Success);
        Assert.IsTrue((await api.SearchSuppliersAsync(request)).Success);
        var customerId = Guid.NewGuid(); var supplierId = Guid.NewGuid();
        Assert.IsTrue((await api.SearchProductsAsync(new ProcurementProductSearchRequest
        { Keyword = request.Keyword, PageIndex = request.PageIndex, PageSize = request.PageSize, CustomerId = customerId, SupplierId = supplierId })).Success);
        Assert.IsTrue((await api.SearchCustomerHistoryFiltersAsync(request)).Success);
        Assert.IsTrue((await api.SearchSupplierHistoryFiltersAsync(request)).Success);
        CollectionAssert.AreEqual(new[] {
            "/customers/candidates/search",
            "/suppliers/candidates/search",
            "/products/candidates/search",
            "/customers/history-filter/search",
            "/suppliers/history-filter/search"
        }, handler.Paths.ToArray());
        Assert.AreEqual(5, handler.Bodies.Count);
        for (var index = 0; index < handler.Bodies.Count; index++)
        {
            using var json = JsonDocument.Parse(handler.Bodies[index]);
            var properties = json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            CollectionAssert.AreEquivalent(index == 2
                ? new[] { "keyword", "pageIndex", "pageSize", "customerId", "supplierId" }
                : new[] { "keyword", "pageIndex", "pageSize" }, properties.Keys.ToArray());
            Assert.AreEqual("阀门", properties["keyword"].GetString());
            Assert.AreEqual(2, properties["pageIndex"].GetInt32());
            Assert.AreEqual(5, properties["pageSize"].GetInt32());
            if (index == 2)
            {
                Assert.AreEqual(customerId, properties["customerId"].GetGuid());
                Assert.AreEqual(supplierId, properties["supplierId"].GetGuid());
            }
        }
    }

    /// <summary>客户端按 HTTP 状态区别权限、输入和系统错误，不依赖任意语言的消息内容。</summary>
    [TestMethod]
    public async Task RefitErrorsUseHttpStatusNotMessageParsing()
    {
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
        {
            using var handler = new RecordingHandler(status);
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            var api = RestService.For<IProcurementMasterDataQueryApi>(client);
            var exception = await Assert.ThrowsAsync<ApiException>(() => api.SearchCustomersAsync(new()));
            Assert.AreEqual(status, exception.StatusCode);
        }
    }

    /// <summary>公开查询结果只暴露身份、显示、必要单位或历史启用标志；不含价格、电话、地址和文件路径。</summary>
    [TestMethod]
    public void ResponsesHaveOnlyApprovedFields()
    {
        CollectionAssert.AreEquivalent(new[] { "Uid", "CustomerCode", "CustomerName" },
            typeof(ProcurementCustomerCandidateDto).GetProperties().Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Uid", "SupplierCode", "SupplierName" },
            typeof(ProcurementSupplierCandidateDto).GetProperties().Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Uid", "Sku", "DescriptionZh", "DescriptionEn", "Unit", "MainImage", "CustomerSku", "LinkSku", "SupplierSku", "FinishZh", "FeaturesZh", "MaterialType", "SupplyType", "UnitPrice", "TaxPrice" },
            typeof(ProcurementProductCandidateDto).GetProperties().Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Uid", "CustomerCode", "CustomerName", "IsActive" },
            typeof(ProcurementCustomerFilterDto).GetProperties().Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "Uid", "SupplierCode", "SupplierName", "IsActive" },
            typeof(ProcurementSupplierFilterDto).GetProperties().Select(p => p.Name).ToArray());
    }

    /// <summary>补充检查 Controller 路由与 Refit 对齐、保持认证边界；不将此静态检查当运行时授权证据。</summary>
    [TestMethod]
    public void ControllerRoutesMatchPublicContract()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs"));
        StringAssert.Contains(source, "[Authorize]");
        StringAssert.Contains(source, "[Route(\"api/master-data/procurement-lookups\")]");
        foreach (var method in typeof(IProcurementMasterDataQueryApi).GetMethods())
        {
            var route = method.GetCustomAttributes(typeof(PostAttribute), false).Cast<PostAttribute>().Single().Path;
            StringAssert.Contains(source, "[HttpPost(\"" + route.TrimStart('/') + "\")]");
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType);
        }
    }

    /// <summary>固定身份测试对象，不注入角色声明。</summary>
    private sealed class TestUser(bool authenticated, Guid? uid) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => uid;
        public string UserName => "候选测试";
    }

    /// <summary>任何进入数据库端口的操作立即失败，以证明早期守卫有效。</summary>
    private sealed class UnreachableRepository : IProcurementMasterDataQueryRepository
    {
        public Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(PagedRequestBase request, CancellationToken ct)
            => throw new AssertFailedException("不应访问仓储。");
        public Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken ct)
            => throw new AssertFailedException("不应访问仓储。");
        public Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(ProcurementProductSearchRequest request, CancellationToken ct)
            => throw new AssertFailedException("不应访问仓储。");
        public Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(PagedRequestBase request, CancellationToken ct)
            => throw new AssertFailedException("不应访问仓储。");
        public Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(PagedRequestBase request, CancellationToken ct)
            => throw new AssertFailedException("不应访问仓储。");
    }

    /// <summary>在进程内记录真实 Refit 请求并返回确定响应，不发起网络访问。</summary>
    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];

        /// <summary>验证请求方法，保留序列化正文供断言，错误消息刻意与业务文案无关。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(status == HttpStatusCode.OK
                    ? """{"success":true,"data":{"items":[],"totalCount":0,"pageIndex":2,"pageSize":5},"message":"OK"}"""
                    : """{"success":false,"message":"任意语言 / arbitrary text"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
