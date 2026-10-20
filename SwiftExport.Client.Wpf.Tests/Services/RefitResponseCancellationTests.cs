using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.Wpf.Tests.Services;

/// <summary>以真实 HTTP 响应流复现读取响应期间取消，不访问业务服务或数据库。</summary>
[TestClass]
public sealed class RefitResponseCancellationTests
{
    /// <summary>正常产品响应仍可连续读取并回填候选，不因取消兼容处理丢失成功结果。</summary>
    [TestMethod]
    public async Task SuccessfulProductQueries_KeepCandidates()
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>.Ok(
            new() { Items = [new() { Uid = Guid.NewGuid(), Sku = "LOOKUP-OK" }] }));
        using var client = new HttpClient(new ResponseHandler(payload)) { BaseAddress = new Uri("https://lookup.test") };
        var api = RestService.For<IProcurementMasterDataQueryApi>(client);
        using var viewModel = new PurchaseOrderDetailViewModel(null!, api, null!, null!, null!, new CurrentUserService());
        await viewModel.SearchProductsCommand.ExecuteAsync("first");
        await viewModel.SearchProductsCommand.ExecuteAsync("second");
        Assert.HasCount(1, viewModel.ProductCandidates);
        Assert.AreEqual("LOOKUP-OK", viewModel.ProductCandidates[0].Sku);
        Assert.IsFalse(viewModel.IsProductSearchBusy);
    }

    /// <summary>未取消时真正的 JSON 损坏仍返回失败说明，不能被当作正常取消吞掉。</summary>
    [TestMethod]
    public async Task InvalidJsonWithoutCancellation_RemainsReported()
    {
        using var client = new HttpClient(new ResponseHandler("{broken")) { BaseAddress = new Uri("https://lookup.test") };
        var api = RestService.For<IProcurementMasterDataQueryApi>(client);
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        var result = await runner.RunAsync(() => api.SearchProductsAsync(new(), CancellationToken.None),
            CancellationToken.None, "查询采购产品失败");
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Message));
    }

    /// <summary>Refit 缓冲中取消可能包装为流已消费；查询调用者已取消时应安静结束。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationDuringResponseBody_ReturnsSilentFailure(bool throughProductSearch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var bodyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServePartialResponseAsync(listener, bodyStarted, deadline.Token);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var api = RestService.For<IProcurementMasterDataQueryApi>(client);
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        using var viewModel = new PurchaseOrderDetailViewModel(null!, api, null!, null!, null!,
            new CurrentUserService(), apiCallRunner: runner);
        ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>? result = null;
        async Task RunThroughRunnerAsync() => result = await runner.RunAsync(
            async () =>
            {
                try { return await api.SearchProductsAsync(new ProcurementProductSearchRequest(), cancellation.Token); }
                catch (ApiException exception)
                {
                    Console.WriteLine($"响应阶段异常：{exception.InnerException?.GetType().Name}：{exception.InnerException?.Message}");
                    throw;
                }
            },
            cancellation.Token, "查询产品失败");
        var query = throughProductSearch
            ? viewModel.SearchProductsCommand.ExecuteAsync("partial")
            : RunThroughRunnerAsync();
        try
        {
            await bodyStarted.Task.WaitAsync(deadline.Token);
            // 服务端不再发送正文，给客户端进入正文缓冲读取的时间，再模拟新搜索替代旧搜索。
            await Task.Delay(150, deadline.Token);
            if (throughProductSearch) viewModel.Dispose();
            else cancellation.Cancel();
            await query.WaitAsync(deadline.Token);
            if (throughProductSearch)
            {
                Assert.HasCount(0, viewModel.ProductCandidates);
                Assert.IsFalse(viewModel.IsProductSearchBusy);
            }
            else
            {
                Assert.IsNotNull(result);
                Assert.IsFalse(result.Success);
                Assert.AreEqual(string.Empty, result.Message);
            }
        }
        finally
        {
            deadline.Cancel();
            await server;
        }
    }

    /// <summary>接收请求后只发送部分正文，让取消发生在响应读取阶段而不是连接阶段。</summary>
    private static async Task ServePartialResponseAsync(TcpListener listener, TaskCompletionSource started, CancellationToken token)
    {
        try
        {
            using var socket = await listener.AcceptTcpClientAsync(token);
            await using var stream = socket.GetStream();
            var buffer = new byte[4096];
            var request = new StringBuilder();
            while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var count = await stream.ReadAsync(buffer, token);
                if (count == 0) throw new IOException("测试请求提前关闭。");
                request.Append(Encoding.ASCII.GetString(buffer, 0, count));
            }
            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 10000\r\nConnection: close\r\n\r\n{\"success\":true,");
            await stream.WriteAsync(response, token);
            await stream.FlushAsync(token);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    /// <summary>为正常与损坏正文测试提供独立响应对象，不共享或预读内容流。</summary>
    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        /// <summary>每个请求返回新的 JSON 内容，保持真实 Refit 反序列化路径。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
