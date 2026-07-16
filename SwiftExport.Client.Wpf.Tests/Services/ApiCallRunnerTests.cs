using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
