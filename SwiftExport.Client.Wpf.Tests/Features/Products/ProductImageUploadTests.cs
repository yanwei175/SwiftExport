using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Files;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Files.ProductImages;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.UI;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Products.ProductList.Views;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证上传进度以服务端结果推进、停止边界、管理员门禁和真实 multipart 契约。</summary>
[TestClass]
public sealed class ProductImageUploadTests
{
    /// <summary>真实 XAML 的进度条和按钮绑定响应上传状态。</summary>
    [TestMethod]
    public void ViewBindings_ShouldFollowUploadProgress() => StaTest.RunAsync(async () =>
    {
        var pending = new TaskCompletionSource<ServiceResult<ProductImageBatchUploadResultDto>>();
        var vm = Create(new UploadClient(_ => pending.Task));
        vm.Prepare(["a.png"]);
        var view = new ProductImageUploadView { DataContext = vm };
        var grid = (Grid)view.Content;
        var progress = grid.Children.OfType<ProgressBar>().Single();
        var actions = grid.Children.OfType<StackPanel>().Single();
        var select = (Button)actions.Children[0];
        var start = (Button)actions.Children[1];
        var stop = (Button)actions.Children[2];
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreSame(vm.StartCommand, start.Command);
        Assert.AreEqual(1d, progress.Maximum);
        var running = vm.StartCommand.ExecuteAsync(null);
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.IsFalse(start.IsEnabled);
        Assert.IsFalse(select.IsEnabled);
        Assert.IsTrue(stop.IsEnabled);
        Assert.AreEqual(0d, progress.Value);
        pending.SetResult(Result(true));
        await running;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual(1d, progress.Value);
        Assert.IsFalse(stop.IsEnabled);
        Assert.IsTrue(select.IsEnabled);
    });

    /// <summary>空窗口不能上传，选择和重新选择可在同一窗口开启新的批次。</summary>
    [TestMethod]
    public async Task Selection_ShouldInitializeAndResetCompletedBatch()
    {
        var vm = Create(new UploadClient(_ => Task.FromResult(Result(true))));
        vm.Prepare([]);
        Assert.IsFalse(vm.StartCommand.CanExecute(null));
        Assert.IsTrue(vm.CanSelectFiles);
        vm.Prepare(["a.png"]);
        Assert.IsTrue(vm.StartCommand.CanExecute(null));
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(1, vm.Completed);
        vm.Prepare(["b.png", "c.png"]);
        Assert.AreEqual(0, vm.Completed);
        Assert.AreEqual("b.png", vm.Items[0].FileName);
        Assert.AreEqual("等待上传", vm.Items[0].Status);
        Assert.IsTrue(vm.StartCommand.CanExecute(null));
    }

    /// <summary>单项拒绝不阻断后项，同名目标不会在同次选择中再次覆盖。</summary>
    [TestMethod]
    public async Task PartialFailureAndDuplicate_ShouldKeepResultsAndProcessNext()
    {
        var client = new UploadClient(path => Task.FromResult(Result(path != "bad.png")));
        var vm = Create(client);
        vm.Prepare(["bad.png", "good.png", "GOOD.webp"]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(2, client.Calls);
        Assert.AreEqual(3, vm.Completed);
        StringAssert.StartsWith(vm.Items[0].Status, "失败：");
        StringAssert.StartsWith(vm.Items[1].Status, "上传成功");
        StringAssert.Contains(vm.Items[2].Status, "相同目标");
        Assert.IsTrue(vm.CanClose(DialogResult.Close));
        Assert.IsFalse(vm.StartCommand.CanExecute(null));
    }

    /// <summary>等待服务端期间不提前推进进度、不允许重入或关闭；停止只阻断后续项目。</summary>
    [TestMethod]
    public async Task Stop_ShouldWaitForCurrentAndPreventReentry()
    {
        var pending = new TaskCompletionSource<ServiceResult<ProductImageBatchUploadResultDto>>();
        var client = new UploadClient(_ => pending.Task);
        var vm = Create(client);
        vm.Prepare(["a.png", "b.png"]);
        var running = vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(0, vm.Completed);
        Assert.IsFalse(vm.CanClose(DialogResult.Close));
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(1, client.Calls);
        vm.StopCommand.Execute(null);
        Assert.IsTrue(vm.IsUploading);
        pending.SetResult(Result(true));
        await running;
        Assert.AreEqual(1, vm.Completed);
        Assert.AreEqual(1, client.Calls);
        Assert.AreEqual("未上传", vm.Items[1].Status);
        Assert.IsTrue(vm.CanClose(DialogResult.Close));
    }

    /// <summary>结果未知不能自动重试或继续覆盖下一张。</summary>
    [TestMethod]
    public async Task UnknownOutcome_ShouldStopWithoutRetry()
    {
        var client = new UploadClient(_ => Task.FromResult(
            ServiceResult<ProductImageBatchUploadResultDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "timeout")));
        var vm = Create(client);
        vm.Prepare(["a.png", "b.png"]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(1, client.Calls);
        StringAssert.StartsWith(vm.Items[0].Status, "结果未知");
        Assert.AreEqual("未上传", vm.Items[1].Status);
    }

    /// <summary>普通用户不能开始上传，客户端门禁不会向服务端发送文件。</summary>
    [TestMethod]
    public async Task NonAdmin_ShouldNotUpload()
    {
        var client = new UploadClient(_ => Task.FromResult(Result(true)));
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { Roles = [] });
        var vm = Create(client, user);
        vm.Prepare(["a.png"]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(0, client.Calls);
        Assert.IsTrue(vm.CanClose(DialogResult.Close));
    }

    /// <summary>会话在等待第一张期间发生替换，剩余项目不会使用新身份继续提交。</summary>
    [TestMethod]
    public async Task ChangedIdentity_ShouldStopRemainingFiles()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { IsAdmin = true, Roles = ["Admin"] });
        var client = new UploadClient(_ =>
        {
            user.SetCurrentUser(new MeDto { IsAdmin = true, Roles = ["Admin"] });
            return Task.FromResult(Result(true));
        });
        var vm = Create(client, user);
        vm.Prepare(["a.png", "b.png"]);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.AreEqual(1, client.Calls);
        Assert.AreEqual("未上传", vm.Items[1].Status);
    }

    /// <summary>Refit 实际发送控制器相对路由和 files 流字段，保留 API 虚拟目录。</summary>
    [TestMethod]
    public async Task Multipart_ShouldUseExistingEndpointAndFileField()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/erp/api/product-images") };
        var api = RestService.For<IProductImageApi>(http);
        using var stream = new System.IO.MemoryStream([1, 2, 3]);
        var result = await api.UploadAsync([new StreamPart(stream, "a.png", "application/octet-stream")]);
        Assert.IsTrue(result.Success);
        Assert.AreEqual("/erp/api/product-images/batch", handler.Path);
        Assert.AreEqual("files", handler.PartName);
        Assert.AreEqual("a.png", handler.FileName);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, handler.Bytes);
    }

    /// <summary>用当前服务端 IsAdmin 投影模拟管理员，不把角色名称当作授权依据。</summary>
    private static ProductImageUploadViewModel Create(UploadClient client, CurrentUserService? user = null)
    {
        if (user is null)
        {
            user = new CurrentUserService();
            user.SetCurrentUser(new MeDto { IsAdmin = true, Roles = ["Admin"] });
        }
        return new(client, user, NullLogger<ProductImageUploadViewModel>.Instance);
    }

    private static ServiceResult<ProductImageBatchUploadResultDto> Result(bool success) =>
        ServiceResult<ProductImageBatchUploadResultDto>.Ok(new ProductImageBatchUploadResultDto
        {
            Total = 1, Succeeded = success ? 1 : 0, Failed = success ? 0 : 1,
            Items = [new ProductImageUploadItemResultDto { Success = success, Message = "图片损坏" }]
        });

    private sealed class UploadClient(Func<string, Task<ServiceResult<ProductImageBatchUploadResultDto>>> upload) : IProductImageUploadClient
    {
        public int Calls { get; private set; }
        public Task<ServiceResult<ProductImageBatchUploadResultDto>> UploadAsync(string path)
        {
            Calls++;
            return upload(path);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? PartName { get; private set; }
        public string? FileName { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            var part = ((MultipartFormDataContent)request.Content!).Single();
            PartName = part.Headers.ContentDisposition!.Name!.Trim('"');
            FileName = part.Headers.ContentDisposition.FileName!.Trim('"');
            Bytes = await part.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"data\":{\"total\":1,\"succeeded\":1,\"failed\":0,\"items\":[]}}")
            };
        }
    }
}
