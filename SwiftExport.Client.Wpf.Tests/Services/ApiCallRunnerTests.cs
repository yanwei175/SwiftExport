using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Services;

[TestClass]
public sealed class ApiCallRunnerTests
{
    [TestMethod]
    public async Task CallerCancellation_ShouldReturnSilentFailure()
    {
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await runner.RunAsync(
            () => Task.FromCanceled<ServiceResult<int>>(cancellation.Token),
            cancellation.Token,
            "加载失败");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.Message);
    }

    [TestMethod]
    public async Task TimeoutWithoutCallerCancellation_ShouldKeepTimeoutMessage()
    {
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);

        var result = await runner.RunAsync<int>(
            () => Task.FromException<ServiceResult<int>>(new TaskCanceledException()),
            CancellationToken.None,
            "加载失败");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "请求超时或已取消");
    }

    [TestMethod]
    public async Task RefitWrappedCallerCancellation_ShouldReturnSilentFailure()
    {
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = CreateApiRequestException(new TaskCanceledException("The operation was canceled."));

        var result = await runner.RunAsync<int>(
            () => Task.FromException<ServiceResult<int>>(exception),
            cancellation.Token,
            "加载失败");

        Assert.IsFalse(result.Success);
        Assert.AreEqual(string.Empty, result.Message);
    }

    [TestMethod]
    public async Task RefitWrappedTimeoutWithoutCallerCancellation_ShouldKeepTimeoutMessage()
    {
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        var exception = CreateApiRequestException(new TaskCanceledException("The operation was canceled."));

        var result = await runner.RunAsync<int>(
            () => Task.FromException<ServiceResult<int>>(exception),
            CancellationToken.None,
            "加载失败");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "请求超时或已取消");
    }

    /// <summary>构造与 Refit 13 发送阶段一致的请求包装异常。</summary>
    private static ApiRequestException CreateApiRequestException(Exception innerException) => new(
        new HttpRequestMessage(HttpMethod.Get, "https://localhost/test"),
        HttpMethod.Get,
        new RefitSettings(),
        innerException);
}
