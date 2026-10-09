using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Application.Tests.WebApi;

/// <summary>通过真实 Controller、JWT、Newtonsoft 和 OpenAPI 出口验收首批 Web 接口；不证明业务库事务。</summary>
[TestClass]
public sealed class FirstWebHttpContractTests
{
    private FirstWebContractHost _host = null!;

    /// <summary>每个场景使用独立宿主和内存防抖缓存。</summary>
    [TestInitialize]
    public async Task StartAsync() { _host = new(); await _host.StartAsync(); }

    /// <summary>释放本场景监听和 HTTP 客户端。</summary>
    [TestCleanup]
    public async Task StopAsync() => await _host.DisposeAsync();

    /// <summary>登录/me 使用裸 DTO，并实际签发校验 JWT；凭据版本变化和篡改令牌均拒绝。</summary>
    [TestMethod]
    public async Task LoginAndMe_UseRealJwtAndBareDtos()
    {
        using var bad = await Post("api/auth/login", "{\"userName\":\"contract-user\",\"password\":\"bad\"}");
        await Failure(bad, 401, "UNAUTHENTICATED");
        await Login();
        Assert.AreEqual("contract-user", _host.Login!.UserName);
        using var me = await _host.Client.GetAsync("api/auth/me");
        Assert.AreEqual(HttpStatusCode.OK, me.StatusCode);
        var body = JObject.Parse(await me.Content.ReadAsStringAsync());
        Assert.IsNull(body["success"]);
        Assert.AreEqual(_host.UserId.ToString(), (string?)body["userId"]);
        Assert.AreEqual(JTokenType.Null, body["nickName"]!.Type);
        Assert.IsTrue(body["permissions"]!.Values<string>().Contains("Products.Product.View"));
        _host.CredentialValid = false;
        using var stale = await _host.Client.GetAsync("api/auth/me");
        await Failure(stale, 401, "UNAUTHENTICATED");
        _host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid.jwt.signature");
        using var invalid = await _host.Client.GetAsync("api/auth/me");
        await Failure(invalid, 401, "UNAUTHENTICATED");
    }

    /// <summary>登录空字段使用真实服务的请求验证，损坏 JSON/空正文由真实模型绑定拒绝，均不访问账号库。</summary>
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"userName\":\"user\",\"password\":null}")]
    [DataRow("{broken-json")]
    [DataRow("null")]
    public async Task Login_RejectsMissingFieldsAndMalformedJson(string json)
    {
        using var response = await Post("api/auth/login", json);
        await Failure(response, 400, "VALIDATION");
    }

    /// <summary>分页参数经真实 Newtonsoft 绑定，返回 Guid、UTC 日期和显式 null。</summary>
    [TestMethod]
    public async Task ProductPage_BindsFiltersAndSerializesActualFields()
    {
        await Login();
        using var response = await Post("api/Product/page", "{\"keyword\":\"SKU\",\"pageIndex\":2,\"pageSize\":30,\"categoryIds\":[\"49cdbca0-ef30-488d-9d05-2c25d2308ef7\"]}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, _host.Search!.PageIndex);
        Assert.AreEqual(30, _host.Search.PageSize);
        Assert.AreEqual("SKU", _host.Search.Keyword);
        Assert.AreEqual(_host.UserId, _host.Search.CategoryIds.Single());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(JTokenType.Null, body["error"]!.Type);
        Assert.AreEqual(JTokenType.Null, body.SelectToken("data.items[0].unit")!.Type);
        Assert.AreEqual(_host.UserId.ToString(), (string?)body.SelectToken("data.items[0].uid"));
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "2026-10-07T01:02:03Z");
        Assert.AreEqual(1, (int?)body.SelectToken("data.totalPages"));
        Assert.AreEqual("Products.Product.View", _host.CheckedPermissions.Single());
    }

    /// <summary>未登录、无权限、格式错误均在调用业务前短路，保留统一错误出口。</summary>
    [TestMethod]
    public async Task ProductPage_RejectsAnonymousDeniedAndMalformedRequests()
    {
        using var anonymous = await Post("api/Product/page", "{}");
        await Failure(anonymous, 401, "UNAUTHENTICATED");
        await Login();
        _host.Permissions.Clear();
        using var denied = await Post("api/Product/page", "{}");
        await Failure(denied, 403, "FORBIDDEN");
        _host.Permissions.Add("Products.Product.View");
        using var malformed = await Post("api/Product/page", "{\"categoryIds\":[\"not-guid\"]}");
        await Failure(malformed, 400, "VALIDATION");
        using var missingBody = await Post("api/Product/page", "null");
        await Failure(missingBody, 400, "VALIDATION");
        Assert.AreEqual(0, _host.BusinessCalls);
    }

    /// <summary>混合批次检查三种实际操作权限，外层身份/版本原样绑定，成功保存行枚举为字符串。</summary>
    [TestMethod]
    public async Task BatchSave_ChecksEveryOperationAndBindsWrapperVersion()
    {
        await Login();
        const string json = "{\"createItems\":[{\"clientKey\":\"create-1\",\"data\":{\"sku\":\"A\"}}],\"updateItems\":[{\"uid\":\"49cdbca0-ef30-488d-9d05-2c25d2308ef7\",\"version\":7,\"data\":{\"version\":99}}],\"deleteItems\":[{\"uid\":\"49cdbca0-ef30-488d-9d05-2c25d2308ef7\",\"version\":7}]}";
        _host.Permissions.Remove("Products.Product.Delete");
        using var denied = await Post("api/Product/batch-save", json);
        await Failure(denied, 403, "FORBIDDEN");
        Assert.AreEqual(0, _host.BusinessCalls);
        _host.Permissions.Add("Products.Product.Delete");
        using var saved = await Post("api/Product/batch-save", json);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        Assert.AreEqual(7, _host.Batch!.UpdateItems.Single().Version);
        Assert.AreEqual(99, _host.Batch.UpdateItems.Single().Data.Version);
        Assert.IsTrue(_host.Batch.DeleteItems.Single().IsSoftDelete);
        var body = JObject.Parse(await saved.Content.ReadAsStringAsync());
        Assert.AreEqual("Create", (string?)body.SelectToken("data.items[0].operation"));
        Assert.AreEqual("Update", (string?)body.SelectToken("data.items[1].operation"));
        Assert.AreEqual("Delete", (string?)body.SelectToken("data.items[2].operation"));
        Assert.AreEqual(3, (int?)body.SelectToken("data.totalCount"));
        Assert.AreEqual(true, (bool?)body.SelectToken("data.allSucceeded"));
        Assert.AreEqual(JTokenType.Null, body.SelectToken("data.items[0].oldVersion")!.Type);
        CollectionAssert.AreEqual(new[] { "Products.Product.Create", "Products.Product.Edit", "Products.Product.Delete", "Products.Product.Create", "Products.Product.Edit", "Products.Product.Delete" }, _host.CheckedPermissions);
        using var throttled = await Post("api/Product/batch-save", json);
        await Failure(throttled, 429, "RATE_LIMITED");
        Assert.IsNotNull(throttled.Headers.RetryAfter);
    }

    /// <summary>服务模拟失败只验证全局映射；真实产品版本拒绝可能是 BUSINESS_BLOCKED，由已有服务测试独立确认。</summary>
    [TestMethod]
    [DataRow("VERSION_CONFLICT", 409)]
    [DataRow("BUSINESS_BLOCKED", 422)]
    [DataRow("OUTCOME_UNKNOWN", 503)]
    [DataRow("SERVICE_UNAVAILABLE", 503)]
    [DataRow("THROW", 500)]
    public async Task BatchSave_UsesStableErrorExit(string code, int status)
    {
        await Login();
        _host.FailureCode = code;
        using var response = await Post("api/Product/batch-save", "{\"createItems\":[{\"data\":{\"sku\":\"A\"}}]}");
        await Failure(response, status, code == "THROW" ? "UNEXPECTED" : code);
        Assert.AreEqual(1, _host.BusinessCalls);
    }

    /// <summary>分类和 Unit 字典读取保留各自权限，不能仅凭产品 View 访问。</summary>
    [TestMethod]
    [DataRow("api/ProductCategory/all", "Products.Category.View", "categoryName")]
    [DataRow("api/SysDictData/by-code/Unit", "System.Dictionary.View", "dictCode")]
    [DataRow("api/ProductColor/all", "Products.Color.View", "uid")]
    public async Task References_RequireTheirOwnPermission(string path, string permission, string field)
    {
        await Login();
        using var allowed = await _host.Client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        var body = JObject.Parse(await allowed.Content.ReadAsStringAsync());
        Assert.IsNotNull(body.SelectToken("data[0]." + field));
        _host.Permissions.Remove(permission);
        using var denied = await _host.Client.GetAsync(path);
        await Failure(denied, 403, "FORBIDDEN");
        Assert.AreEqual(1, _host.BusinessCalls);
    }

    /// <summary>供应商 POST 与仓库 GET 分别走实际绑定方式，使用自身查看权限。</summary>
    [TestMethod]
    public async Task ProductPickers_BindQueryAndBodyWithExactPermissions()
    {
        await Login();
        using var warehouse = await _host.Client.GetAsync("api/product/default-warehouse-candidates?keyword=WH&pageIndex=2&pageSize=40");
        Assert.AreEqual(HttpStatusCode.OK, warehouse.StatusCode);
        Assert.AreEqual(2, _host.WarehouseSearch!.PageIndex);
        Assert.AreEqual(40, _host.WarehouseSearch.PageSize);
        Assert.AreEqual("WH", _host.WarehouseSearch.Keyword);
        using var suppliers = await Post("api/supplier/page", "{\"keyword\":\"SUP\",\"pageIndex\":1,\"pageSize\":30}");
        Assert.AreEqual(HttpStatusCode.OK, suppliers.StatusCode);
        Assert.AreEqual("SUP", _host.SupplierSearch!.Keyword);
        Assert.AreEqual(30, _host.SupplierSearch.PageSize);
        _host.Permissions.Remove("Suppliers.Supplier.View");
        using var denied = await Post("api/supplier/page", "{}");
        await Failure(denied, 403, "FORBIDDEN");
        _host.Permissions.Remove("Products.Product.View");
        using var deniedWarehouse = await _host.Client.GetAsync("api/product/default-warehouse-candidates");
        await Failure(deniedWarehouse, 403, "FORBIDDEN");
        Assert.AreEqual(2, _host.BusinessCalls);
    }

    /// <summary>空批次和损坏 JSON 不进入写服务；读写结果均遵循全局错误出口。</summary>
    [TestMethod]
    public async Task BatchSave_RejectsEmptyAndMalformedBodiesBeforeService()
    {
        await Login();
        foreach (var json in new[] { "{}", "{broken-json", "{\"deleteItems\":[{\"uid\":\"invalid\"}]}" })
        {
            using var response = await Post("api/product/batch-save", json);
            await Failure(response, 400, "VALIDATION");
        }
        Assert.AreEqual(0, _host.BusinessCalls);
    }

    /// <summary>实际导出生产生成器 OpenAPI，并检查 JSON 命名、null、枚举、泛型 ID、安全与裸认证 DTO。</summary>
    [TestMethod]
    public async Task OpenApi_MatchesFirstBatchJsonAndExports()
    {
        using var response = await _host.Client.GetAsync("swagger/v1/swagger.json");
        var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json);
        var doc = JObject.Parse(json);
        var schemas = (JObject)doc["components"]!["schemas"]!;
        var export = Environment.GetEnvironmentVariable("SWIFTEXPORT_WEB_OPENAPI_OUTPUT");
        if (!string.IsNullOrWhiteSpace(export)) await File.WriteAllTextAsync(Path.GetFullPath(export), json, new UTF8Encoding(false));
        Assert.IsTrue(schemas.Properties().All(p => System.Text.RegularExpressions.Regex.IsMatch(p.Name, "^[a-zA-Z0-9._-]+$")), "泛型组件 ID 必须符合 OpenAPI 引用名称规则。");
        var operation = schemas.Properties().Single(p => p.Name.EndsWith("SaveOperation", StringComparison.Ordinal)).Value;
        Assert.AreEqual("string", (string?)operation["type"]);
        CollectionAssert.AreEqual(new[] { "Create", "Update", "Delete" }, operation["enum"]!.Values<string>().ToArray());
        var product = schemas["SwiftExport.Contracts.DTOs.Products.ProductDto"]!;
        Assert.AreEqual("uuid", (string?)product.SelectToken("properties.uid.format"));
        Assert.AreEqual("date-time", (string?)product.SelectToken("properties.createdDate.format"));
        Assert.AreEqual(true, (bool?)product.SelectToken("properties.unit.nullable"));
        var wrapper = schemas["SwiftExport.Contracts.Wrappers.ServiceResultOfSwiftExport.Contracts.DTOs.Products.Responses.ProductBatchSaveResponse"]!;
        Assert.AreEqual(true, (bool?)wrapper.SelectToken("properties.error.nullable"), "成功响应 error 实际为 null。");
        Assert.AreEqual(true, (bool?)wrapper.SelectToken("properties.data.nullable"), "失败响应 data 实际为 null。");
        var login = doc["paths"]!["/api/auth/login"]!["post"]!;
        StringAssert.EndsWith((string)login.SelectToken("responses.200.content['application/json'].schema['$ref']")!, "LoginTokenDto");
        Assert.AreEqual(0, ((JArray)login["security"]!).Count);
        var me = doc["paths"]!["/api/auth/me"]!["get"]!;
        StringAssert.EndsWith((string)me.SelectToken("responses.200.content['application/json'].schema['$ref']")!, "MeDto");
        Assert.IsNotNull(me.SelectToken("security[0].Bearer"));
        Assert.AreEqual("bearer", (string?)doc.SelectToken("components.securitySchemes.Bearer.scheme"));
        var loginSchema = schemas["SwiftExport.Contracts.DTOs.Auth.LoginRequestDto"]!;
        CollectionAssert.AreEquivalent(new[] { "userName", "password" }, loginSchema["required"]!.Values<string>().ToArray());
        Assert.AreNotEqual(true, (bool?)loginSchema.SelectToken("properties.password.nullable"));
        var csrf = doc["paths"]!["/api/auth/browser-session/csrf"]!["get"]!;
        Assert.AreEqual("getBrowserSessionCsrf", (string?)csrf["operationId"]);
        Assert.AreEqual(0, ((JArray)csrf["security"]!).Count);
        StringAssert.EndsWith((string)csrf.SelectToken("responses.200.content['application/json'].schema['$ref']")!, "BrowserSessionCsrfDto");
        var restore = doc["paths"]!["/api/auth/browser-session/restore"]!["get"]!;
        Assert.AreEqual("restoreBrowserSession", (string?)restore["operationId"]);
        Assert.IsNotNull(restore.SelectToken("security[0].BrowserSessionCookie"));
        Assert.IsNull(restore.SelectToken("security[0].Bearer"));
        var logout = doc["paths"]!["/api/auth/browser-session/logout"]!["post"]!;
        Assert.AreEqual("logoutBrowserSession", (string?)logout["operationId"]);
        Assert.IsNotNull(logout.SelectToken("responses.204"));
        Assert.IsNull(logout.SelectToken("responses.200"));
        Assert.AreEqual("cookie", (string?)doc.SelectToken("components.securitySchemes.BrowserSessionCookie.in"));

        // 权限目录来自真实 Contracts 静态权威源；导出由显式路径控制，不引入运行时权限扩大。
        var codes = PermissionCatalog.All.Select(item => item.PermissionKey).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        Assert.IsTrue(codes.Length > 0 && codes.All(code => !string.IsNullOrWhiteSpace(code)));
        Assert.AreEqual(codes.Length, codes.Distinct(StringComparer.Ordinal).Count(), "权限目录不得含重复键。");
        var permissionExport = Environment.GetEnvironmentVariable("SWIFTEXPORT_WEB_PERMISSIONS_OUTPUT");
        if (!string.IsNullOrWhiteSpace(permissionExport))
            await File.WriteAllTextAsync(Path.GetFullPath(permissionExport), new JObject { ["codes"] = new JArray(codes) }.ToString(), new UTF8Encoding(false));
    }

    /// <summary>通过真实登录获取令牌，后续请求不绕过生产 JWT 中间件。</summary>
    private async Task Login()
    {
        using var response = await Post("api/auth/login", "{\"userName\":\"contract-user\",\"password\":\"contract-password\"}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsNull(body["success"]);
        _host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)body["access_token"]!);
    }

    /// <summary>使用原始 JSON 走框架绑定，避免客户端序列化掩盖服务器契约。</summary>
    private Task<HttpResponseMessage> Post(string path, string json) => _host.Client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>检查统一失败包装与追踪头，保证敏感异常不回传。</summary>
    private static async Task Failure(HttpResponseMessage response, int status, string code)
    {
        Assert.AreEqual(status, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(false, (bool?)body["success"]);
        Assert.AreEqual(JTokenType.Null, body["data"]!.Type);
        Assert.AreEqual(code, (string?)body.SelectToken("error.code"));
        Assert.IsTrue(response.Headers.Contains("X-Correlation-ID"));
        Assert.IsFalse(body.ToString().Contains("secret-contract-probe", StringComparison.Ordinal));
    }
}
