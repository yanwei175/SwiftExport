using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Configuration;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>通过真实 HTTP 管道验证错误协议，避免只测试映射函数而遗漏框架短路出口。</summary>
[TestClass]
public sealed class HttpErrorContractTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>仅启动测试端点，不连接数据库或调用业务服务。</summary>
    [TestInitialize]
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration["Auth:JwtKey"] = "http-contract-tests-only-key-at-least-32-bytes";
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddWebApiServices();
        builder.Services.AddControllers().AddApplicationPart(typeof(ErrorProbeController).Assembly);
        _app = builder.Build();
        _app.UseWebApiPipeline();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    /// <summary>释放本地监听和客户端。</summary>
    [TestCleanup]
    public async Task StopAsync()
    {
        _client?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>覆盖通用分类、值类型 Data 清空、字段路径及追踪头。</summary>
    [TestMethod]
    [DataRow("VALIDATION", 400)]
    [DataRow("UNAUTHENTICATED", 401)]
    [DataRow("FORBIDDEN", 403)]
    [DataRow("NOT_FOUND", 404)]
    [DataRow("VERSION_CONFLICT", 409)]
    [DataRow("COMMAND_CONFLICT", 409)]
    [DataRow("STATE_BLOCKED", 409)]
    [DataRow("COMMAND_BUSY", 409)]
    [DataRow("BUSINESS_BLOCKED", 422)]
    [DataRow("RATE_LIMITED", 429)]
    [DataRow("PROC_BOM_BASIS_CHANGED", 409)]
    [DataRow("SUPPLY_SHORTFALL", 422)]
    [DataRow("SERVICE_UNAVAILABLE", 503)]
    [DataRow("OUTCOME_UNKNOWN", 503)]
    public async Task Failure_UsesHttpStatusAndNullData(string code, int status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"error-probe/failure/{code}");
        request.Headers.Add("X-Correlation-ID", "error-contract-test");
        using var response = await _client.SendAsync(request);
        var body = await AssertFailureAsync(response, status, code);
        Assert.AreEqual("Lines[1].Qty", (string?)body["error"]?["field"]);
        Assert.AreEqual("error-contract-test", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    /// <summary>框架产生的 401、403 和无路由 404 也必须带相同包装。</summary>
    [TestMethod]
    [DataRow("secured", 401, "UNAUTHENTICATED")]
    [DataRow("forbidden", 403, "FORBIDDEN")]
    [DataRow("missing", 404, "NOT_FOUND")]
    public async Task FrameworkFailures_UseEnvelope(string path, int status, string code)
    {
        using var response = await _client.GetAsync($"error-probe/{path}");
        await AssertFailureAsync(response, status, code);
        if (status == 401) Assert.IsTrue(response.Headers.WwwAuthenticate.Any(x => x.Scheme == "Bearer"));
    }

    /// <summary>缺少字段及损坏 JSON 在业务调用前返回安全的参数错误。</summary>
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{broken-json")]
    public async Task ModelBinding_UsesValidation(string json)
    {
        using var response = await _client.PostAsync("error-probe/validate", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await AssertFailureAsync(response, 400, "VALIDATION");
        Assert.IsFalse(body.ToString().Contains("broken-json", StringComparison.Ordinal));
    }

    /// <summary>未处理异常与未登记错误码都不能泄露技术消息。</summary>
    [TestMethod]
    [DataRow("throw")]
    [DataRow("failure/UNREGISTERED_CODE")]
    public async Task TechnicalFailure_IsSafe500(string path)
    {
        using var response = await _client.GetAsync($"error-probe/{path}");
        var body = await AssertFailureAsync(response, 500, "UNEXPECTED");
        Assert.IsFalse(body.ToString().Contains("secret-sql", StringComparison.Ordinal));
        Assert.IsTrue(response.Headers.Contains("X-Correlation-ID"));
    }

    /// <summary>防抖短路返回可识别代码与 Retry-After；首次成功响应不受影响。</summary>
    [TestMethod]
    public async Task DuplicateRequest_Uses429AndRetryAfter()
    {
        using var first = await _client.PostAsync("error-probe/throttle", null);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using var second = await _client.PostAsync("error-probe/throttle", null);
        await AssertFailureAsync(second, 429, "RATE_LIMITED");
        Assert.IsTrue(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    /// <summary>验证失败包装的共同不变量。</summary>
    private static async Task<JObject> AssertFailureAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.AreEqual(status, (int)response.StatusCode);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(false, (bool?)body["success"]);
        Assert.AreEqual(JTokenType.Null, body["data"]?.Type);
        Assert.AreEqual(code, (string?)body["error"]?["code"]);
        Assert.AreEqual((string?)body["message"], (string?)body["error"]?["message"]);
        return body;
    }
}

/// <summary>只随测试程序集加载的协议探针，完全避开业务数据库。</summary>
[ApiController]
[Route("error-probe")]
public sealed class ErrorProbeController : ControllerBase
{
    /// <summary>返回带值类型脏数据的失败，验证 HTTP 出口清空 Data。</summary>
    [HttpGet("failure/{code}")]
    public ServiceResult<int> Failure(string code)
    {
        var result = ServiceResult<int>.Fail(code, "secret-sql", "Lines[1].Qty");
        result.Data = 123;
        return result;
    }

    /// <summary>测试未登录认证边界。</summary>
    [Authorize, HttpGet("secured")]
    public IActionResult Secured() => Ok();

    /// <summary>测试显式权限拒绝出口。</summary>
    [HttpGet("forbidden")]
    public IActionResult Forbidden() => Forbid();

    /// <summary>测试自动模型校验出口。</summary>
    [HttpPost("validate")]
    public IActionResult Validate(ErrorProbeRequest request) => Ok(request);

    /// <summary>测试系统异常出口。</summary>
    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("secret-sql");

    /// <summary>测试动作过滤器短路。</summary>
    [HttpPost("throttle"), PreventDuplicateRequest(WindowSeconds = 60)]
    public ServiceResult<int> Throttle() => ServiceResult<int>.Ok(1);
}

/// <summary>用于框架必填验证的最小请求。</summary>
public sealed class ErrorProbeRequest
{
    /// <summary>必须提供名称才能进入测试动作。</summary>
    [Required]
    public string? Name { get; set; }
}
