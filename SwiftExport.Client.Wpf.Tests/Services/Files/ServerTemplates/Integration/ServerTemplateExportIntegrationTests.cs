using System.IO;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Services.Files.Templates;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.ServerTemplates;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.Integration;

/// <summary>验证服务器清单、Refit、认证下载、客户端缓存与真实 Report 模板生成的完整调用链。</summary>
[TestClass]
public sealed class ServerTemplateExportIntegrationTests
{
    /// <summary>完整客户端组合根包含服务器模板单例，并让下载和列表使用同一 API 虚拟目录。</summary>
    [TestMethod]
    public void ClientRegistration_ShouldResolveServerTemplateServiceAndPreserveVirtualBase()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = "https://example.invalid/virtual/"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSwiftExportClientServices(configuration);
        services.AddSwiftExportApis();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IServerExcelTemplateService>();
        Assert.AreSame(service, provider.GetRequiredService<IServerExcelTemplateService>());
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ServerExcelTemplateService.HttpClientName);
        Assert.AreEqual("https://example.invalid/virtual/api/export-templates/", client.BaseAddress!.AbsoluteUri);
        Assert.IsNotNull(provider.GetRequiredService<IExportTemplateApi>());
    }

    /// <summary>真实 HTTP 下载两千行模板后保留公式、打印范围和页脚，再次导出仅复用下载缓存。</summary>
    [TestMethod]
    public async Task AuthenticatedHttp_ShouldRenderTwoThousandRowsAndReuseValidatedCache()
    {
        await using var host = await ServerTemplateApiHost.StartAsync();
        using var client = new ClientScope(host);
        var contract = new ExportTemplateContract("SO", "detail", 1);
        var info = (await client.Service.ListAsync(contract)).Single();
        Assert.AreEqual("受控测试单据", info.DisplayName);
        Assert.AreEqual(1, info.TemplateVersion);

        var request = new ServerTemplateExportRequest(info.TemplateId, info.TemplateVersion, contract,
            TemplateTestFixture.DetailData(rows: 2000));
        var target = Path.Combine(host.Root, "订单.xlsx");
        await client.Service.ExportToFileAsync(request, target);
        using (var workbook = new XLWorkbook(target))
        {
            var sheet = workbook.Worksheet("订单");
            Assert.AreEqual("CODE-2000", sheet.Cell("B2004").CachedValue.GetText());
            Assert.AreEqual("ROUND(C2004*D2004,2)", sheet.Cell("E2004").FormulaA1);
            Assert.AreEqual("C2004*$D$2", sheet.Cell("G2004").FormulaA1);
            Assert.AreEqual("固定页脚", sheet.Cell("B2006").CachedValue.GetText());
            Assert.AreEqual(2006, sheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.RowNumber);
        }
        await client.Service.ExportToFileAsync(request, Path.Combine(host.Root, "第二份订单.xlsx"));
        Assert.AreEqual(1, host.Downloads);

        using var anonymous = new HttpClient();
        using var denied = await anonymous.GetAsync(new Uri(host.Address,
            "api/export-templates?documentType=SO&dataContractKey=detail&dataContractVersion=1"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var protectedClient = client.Provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ServerExcelTemplateService.HttpClientName);
        using var content = await protectedClient.GetAsync("test-template/versions/1/content");
        Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content.Content.Headers.ContentType!.MediaType);
        Assert.IsTrue(content.Headers.CacheControl!.Private);
        Assert.IsTrue(content.Headers.CacheControl.NoCache);
        Assert.AreEqual("attachment", content.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual(0, content.Headers.AcceptRanges.Count);
        using var invalidVersion = await protectedClient.GetAsync("test-template/versions/invalid/content");
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, "test-template/versions/1/content");
        using var head = await protectedClient.SendAsync(headRequest);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, head.StatusCode);
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "test-template/versions/1/content");
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
        using var range = await protectedClient.SendAsync(rangeRequest);
        Assert.AreEqual(HttpStatusCode.OK, range.StatusCode);
        Assert.AreEqual(info.FileLength, (await range.Content.ReadAsByteArrayAsync()).LongLength);
    }

    /// <summary>已缓存版本被替换或停用时立即停止新导出；刷新后可取得新版本且不自动替换用户选择。</summary>
    [TestMethod]
    public async Task CatalogChanges_ShouldRejectOldSelectionAndDisabledCachedVersion()
    {
        await using var host = await ServerTemplateApiHost.StartAsync();
        using var client = new ClientScope(host);
        var contract = new ExportTemplateContract("SO", "detail", 1);
        var oldRequest = new ServerTemplateExportRequest("test-template", 1, contract, TemplateTestFixture.DetailData());
        await client.Service.ExportToFileAsync(oldRequest, Path.Combine(host.Root, "原导出.xlsx"));
        var protectedTarget = Path.Combine(host.Root, "保护目标.xlsx");
        await File.WriteAllTextAsync(protectedTarget, "原文件");
        await host.PublishAsync("头与明细模板.xlsx", "detail", 2);

        await Assert.ThrowsAsync<FileOperationException>(() =>
            client.Service.ExportToFileAsync(oldRequest, protectedTarget, overwrite: true));
        Assert.AreEqual("原文件", await File.ReadAllTextAsync(protectedTarget));
        var newInfo = (await client.Service.ListAsync(contract)).Single();
        Assert.AreEqual(2, newInfo.TemplateVersion);
        var newRequest = new ServerTemplateExportRequest(newInfo.TemplateId, newInfo.TemplateVersion, contract,
            TemplateTestFixture.DetailData());
        await client.Service.ExportToFileAsync(newRequest, Path.Combine(host.Root, "新版.xlsx"));
        Assert.AreEqual(2, host.Downloads);
        await host.PublishAsync("头与明细模板.xlsx", "detail", 2, enabled: false);
        Assert.HasCount(0, await client.Service.ListAsync(contract));
        await Assert.ThrowsAsync<FileOperationException>(() =>
            client.Service.ExportToFileAsync(newRequest, protectedTarget, overwrite: true));
        Assert.AreEqual("原文件", await File.ReadAllTextAsync(protectedTarget));
        Assert.AreEqual(2, host.Downloads);
    }

    /// <summary>活动账号检查返回 401 后结束真实客户端会话，旧缓存无法用于下一次导出。</summary>
    [TestMethod]
    public async Task InactiveAccount_ShouldEndSessionAndPreserveExistingOutput()
    {
        await using var host = await ServerTemplateApiHost.StartAsync();
        using var client = new ClientScope(host);
        var request = new ServerTemplateExportRequest("test-template", 1,
            new ExportTemplateContract("SO", "detail", 1), TemplateTestFixture.DetailData());
        var target = Path.Combine(host.Root, "已导出.xlsx");
        await client.Service.ExportToFileAsync(request, target);
        var before = await File.ReadAllBytesAsync(target);
        host.DeactivateAccount();
        try
        {
            await client.Service.ExportToFileAsync(request, target, overwrite: true);
            Assert.Fail("停用账号不能凭模板缓存导出。");
        }
        catch (Exception exception) when (exception is FileOperationException or OperationCanceledException) { }
        Assert.IsFalse(client.User.IsAuthenticated);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(target));
        Assert.AreEqual(1, host.Downloads);
    }

    /// <summary>从服务器下载嵌套模板后实际展开分组、小计公式和明细，不依赖客户端业务模块。</summary>
    [TestMethod]
    public async Task NestedTemplate_ShouldKeepGroupsAndSubtotalThroughHttp()
    {
        await using var host = await ServerTemplateApiHost.StartAsync();
        await host.PublishAsync("嵌套分组模板.xlsx", "grouped", 2);
        using var client = new ClientScope(host);
        var data = new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Title"] = "分组单据",
            ["Groups"] = new[]
            {
                new { Name = "外层甲", Items = new[]
                {
                    new { GroupName = "内层甲", Code = "A1", Quantity = 2, Price = 3m },
                    new { GroupName = "内层乙", Code = "A2", Quantity = 4, Price = 5m }
                } },
                new { Name = "外层乙", Items = new[]
                {
                    new { GroupName = "内层丙", Code = "B1", Quantity = 1, Price = 8m }
                } }
            }
        });
        var target = Path.Combine(host.Root, "分组.xlsx");
        await client.Service.ExportToFileAsync(new ServerTemplateExportRequest("test-template", 2,
            new ExportTemplateContract("SO", "grouped", 1), data), target);
        using var output = new MemoryStream(await File.ReadAllBytesAsync(target));
        CollectionAssert.IsSubsetOf(new[] { "外层甲", "外层乙", "A1", "A2", "B1" }, TemplateTestFixture.TextValues(output));
        var ns = TemplateTestFixture.Spreadsheet;
        var xml = TemplateTestFixture.Xml(output, "xl/worksheets/sheet1.xml");
        Assert.IsTrue(xml.Descendants(ns + "f").Count(f => f.Value.StartsWith("Subtotal(9,", StringComparison.OrdinalIgnoreCase)) >= 3);
        Assert.IsTrue(xml.Descendants(ns + "row").Any(row => (int?)row.Attribute("outlineLevel") > 0));
    }

    /// <summary>未实现 Excel 函数保持公式，方向、纸张与打开时重算标记经服务器分发后仍完整。</summary>
    [TestMethod]
    public async Task FormulaTemplate_ShouldPreservePrintSettingsAndRecalculationFlags()
    {
        await using var host = await ServerTemplateApiHost.StartAsync();
        await host.PublishAsync("公式打印模板.xlsx", "formula", 2, includePrintFooter: true);
        using var client = new ClientScope(host);
        var target = Path.Combine(host.Root, "公式.xlsx");
        var data = new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Title"] = "公式单据",
            ["Items"] = new[] { new { Code = "X1", Quantity = 2 }, new { Code = "X2", Quantity = 4 } }
        });
        await client.Service.ExportToFileAsync(new ServerTemplateExportRequest("test-template", 2,
            new ExportTemplateContract("SO", "formula", 1), data), target);
        using var output = new MemoryStream(await File.ReadAllBytesAsync(target));
        var ns = TemplateTestFixture.Spreadsheet;
        var sheet = TemplateTestFixture.Xml(output, "xl/worksheets/sheet1.xml");
        var formulas = sheet.Descendants(ns + "c").Where(c => c.Element(ns + "f") is not null)
            .ToDictionary(c => (string)c.Attribute("r")!, c => c.Element(ns + "f")!.Value);
        Assert.AreEqual("UNSUPPORTED_FUNCTION(1)", formulas["F2"]);
        Assert.AreEqual("UNSUPPORTED_FUNCTION(C5)", formulas["E5"]);
        Assert.AreEqual("ROUND(C5*$B$2,2)", formulas["D5"]);
        Assert.AreEqual("landscape", (string?)sheet.Root!.Element(ns + "pageSetup")!.Attribute("orientation"));
        Assert.AreEqual("9", (string?)sheet.Root.Element(ns + "pageSetup")!.Attribute("paperSize"));
        var footer = sheet.Root.Element(ns + "headerFooter")!.Element(ns + "oddFooter")!.Value;
        StringAssert.Contains(footer, "服务器模板");
        StringAssert.Contains(footer, "&P");
        StringAssert.Contains(footer, "&N");
        var calc = TemplateTestFixture.Xml(output, "xl/workbook.xml").Root!.Element(ns + "calcPr")!;
        Assert.AreEqual("auto", (string?)calc.Attribute("calcMode"));
        Assert.IsTrue(calc.Attribute("fullCalcOnLoad")?.Value is "1" or "true");
        Assert.IsTrue(calc.Attribute("forceFullCalc")?.Value is "1" or "true");
    }

    /// <summary>使用正式 API 注册与 AuthTokenHandler，只把缓存目录和账号存储替换为隔离测试环境。</summary>
    private sealed class ClientScope : IDisposable
    {
        internal ClientScope(ServerTemplateApiHost host)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            User = new CurrentUserService();
            User.SetCurrentUser(new MeDto { UserId = host.UserId, UserName = "测试用户" });
            var tokens = new MemoryTokenStore();
            var session = new SessionService(tokens, User);
            services.AddSingleton<ICurrentUserService>(User);
            services.AddSingleton<ITokenStore>(tokens);
            services.AddSingleton<ISessionService>(session);
            services.AddTransient<AuthTokenHandler>();
            services.Configure<ApiOptions>(options => options.BaseUrl = host.Address.AbsoluteUri);
            services.AddSwiftExportApis();
            services.AddClientFileServices();
            Provider = services.BuildServiceProvider();
            Service = new ServerExcelTemplateService(() => Provider.GetRequiredService<IExportTemplateApi>(),
                Provider.GetRequiredService<IHttpClientFactory>(), User, session,
                Provider.GetRequiredService<IExcelTemplateExportService>(),
                new ServerTemplateCacheOptions { CacheRootPath = Path.Combine(host.Root, "client-cache") });
        }

        internal ServiceProvider Provider { get; }
        internal CurrentUserService User { get; }
        internal ServerExcelTemplateService Service { get; }

        /// <summary>先解绑服务的会话事件，再释放其 HTTP 工厂。</summary>
        public void Dispose()
        {
            Service.Dispose();
            Provider.Dispose();
        }
    }

    /// <summary>仅在测试进程内保存固定凭据，避免读取或修改实际用户登录资料。</summary>
    private sealed class MemoryTokenStore : ITokenStore
    {
        private string? _token = "template-test-token";
        /// <inheritdoc/>
        public bool HasAccessToken => _token is not null;
        /// <inheritdoc/>
        public string? GetAccessToken() => _token;
        /// <inheritdoc/>
        public void SetAccessToken(string accessToken) => _token = accessToken;
        /// <inheritdoc/>
        public void Clear() => _token = null;
    }
}
