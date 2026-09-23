using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>验证 Refit 非成功响应仍保留业务错误，并区分冲突和结果未知。</summary>
[TestClass]
public sealed class ApiErrorResponseTests
{
    /// <summary>失败 Data=null 可用于值类型结果，且保留字段与业务提示。</summary>
    [TestMethod]
    [DataRow(409, "VERSION_CONFLICT")]
    [DataRow(409, "COMMAND_CONFLICT")]
    [DataRow(422, "BUSINESS_BLOCKED")]
    [DataRow(500, "UNEXPECTED")]
    [DataRow(503, "SERVICE_UNAVAILABLE")]
    [DataRow(503, "OUTCOME_UNKNOWN")]
    public async Task Runner_PreservesStructuredFailure(int status, string code)
    {
        var json = $$$"""{"success":false,"message":"原提示","data":null,"error":{"code":"{{{code}}}","message":"原提示","field":"ExpectedVersion"}}""";
        var exception = await CreateAsync(status, json);
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        var result = await runner.RunAsync<int>(() => Task.FromException<ServiceResult<int>>(exception));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual("ExpectedVersion", result.Error.Field);
        Assert.AreEqual("原提示", result.Message);
    }

    /// <summary>HTML 网关错误、残缺或假成功正文不能泄露到 UI，也不能当作明确业务失败。</summary>
    [TestMethod]
    [DataRow(502, "<html>secret</html>", "OUTCOME_UNKNOWN")]
    [DataRow(503, "{", "OUTCOME_UNKNOWN")]
    [DataRow(500, "{\"success\":true}", "OUTCOME_UNKNOWN")]
    [DataRow(409, "", "STATE_BLOCKED")]
    [DataRow(401, "", "UNAUTHENTICATED")]
    [DataRow(403, "", "FORBIDDEN")]
    public async Task InvalidBody_UsesSafeStatusFallback(int status, string body, string expected)
    {
        var result = ApiErrorResponseReader.Read<int>(await CreateAsync(status, body), "保存失败");
        Assert.AreEqual(expected, result.Error.Code);
        Assert.IsFalse(result.Message.Contains("secret", StringComparison.Ordinal));
    }

    /// <summary>构造真实 Refit HTTP 异常，包含服务端响应正文。</summary>
    internal static Task<ApiException> CreateAsync(int status, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/test");
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());
    }
}
