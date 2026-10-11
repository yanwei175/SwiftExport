using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Updates;

namespace SwiftExport.Client.Wpf.Tests.Updates;

/// <summary>验证更新状态迁移、失败保留、取消及互斥，不访问真实服务器。</summary>
[TestClass]
public sealed class ClientUpdateTests
{
    /// <summary>只有下载完成且页面允许离开时才可应用包。</summary>
    [TestMethod]
    public async Task UpdateFlow_RequiresDownloadAndCloseApproval()
    {
        var service = new UpdateStub();
        var guard = new RestartStub { Allow = false };
        using var model = Create(service, guard);
        await model.RestartCommand.ExecuteAsync(null);
        Assert.AreEqual(0, service.Restarts);
        await model.CheckOnStartupAsync();
        Assert.AreEqual("0.1.1", model.AvailableRelease?.Version);
        Assert.IsTrue(model.DownloadCommand.CanExecute(null));
        await model.DownloadCommand.ExecuteAsync(null);
        Assert.AreEqual(100, model.DownloadProgress);
        await model.RestartCommand.ExecuteAsync(null);
        Assert.AreEqual(0, service.Restarts);
        Assert.IsTrue(model.IsReadyToRestart);
        guard.Allow = true;
        await model.RestartCommand.ExecuteAsync(null);
        Assert.AreEqual(1, service.Restarts);
    }

    /// <summary>失败保留版本与下载准入，重试可完成。</summary>
    [TestMethod]
    public async Task Failure_PreservesReleaseAndAllowsRetry()
    {
        var service = new UpdateStub();
        using var model = Create(service);
        await model.CheckOnStartupAsync();
        var release = model.AvailableRelease;
        service.FailDownload = true;
        await model.DownloadCommand.ExecuteAsync(null);
        Assert.AreSame(release, model.AvailableRelease);
        Assert.IsFalse(model.IsReadyToRestart);
        Assert.IsTrue(model.DownloadCommand.CanExecute(null));
        service.FailCheck = true;
        await model.CheckCommand.ExecuteAsync(null);
        Assert.AreSame(release, model.AvailableRelease);
        service.FailDownload = false;
        await model.DownloadCommand.ExecuteAsync(null);
        Assert.IsTrue(model.IsReadyToRestart);
    }

    /// <summary>开发及无配置运行均跳过网络。</summary>
    [TestMethod]
    public async Task DevelopmentAndMissingFeed_SkipNetwork()
    {
        var service = new UpdateStub { IsInstalled = false };
        using var model = Create(service);
        await model.CheckOnStartupAsync();
        Assert.AreEqual(0, service.Checks);
        service.IsInstalled = true;
        service.IsConfigured = false;
        await model.CheckCommand.ExecuteAsync(null);
        Assert.AreEqual(0, service.Checks);
        Assert.IsFalse(model.DownloadCommand.CanExecute(null));
    }

    /// <summary>重复检查只有一个请求，取消后迟到结果不回写。</summary>
    [TestMethod]
    public async Task LateCheck_RejectsDuplicateAndCancelledResult()
    {
        var pending = new TaskCompletionSource<ClientUpdateRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new UpdateStub { CheckTask = pending.Task };
        using var model = Create(service);
        var first = model.CheckOnStartupAsync();
        await model.CheckOnStartupAsync();
        Assert.AreEqual(1, service.Checks);
        model.CancelCurrentOperation();
        pending.SetResult(new("0.1.9", "迟到"));
        await first;
        Assert.IsNull(model.AvailableRelease);
        Assert.IsFalse(model.IsBusy);
    }

    /// <summary>释放中止下载，重复入口不能开放重启。</summary>
    [TestMethod]
    public async Task DisposeDuringDownload_CancelsAndNeverMarksReady()
    {
        var service = new UpdateStub { WaitForCancellation = true };
        var model = Create(service);
        await model.CheckOnStartupAsync();
        var first = model.DownloadCommand.ExecuteAsync(null);
        await model.DownloadCommand.ExecuteAsync(null);
        await model.RestartCommand.ExecuteAsync(null);
        Assert.AreEqual(1, service.Downloads);
        model.Dispose();
        await first;
        Assert.IsFalse(model.IsReadyToRestart);
        Assert.AreEqual(0, service.Restarts);
    }

    /// <summary>本地待应用包直接开放重启，检查失败仍保留它。</summary>
    [TestMethod]
    public async Task PendingPackage_SurvivesFailedCheck()
    {
        var service = new UpdateStub { IsDownloaded = true };
        using var model = Create(service);
        await model.CheckOnStartupAsync();
        Assert.IsTrue(model.IsReadyToRestart);
        service.FailCheck = true;
        await model.CheckCommand.ExecuteAsync(null);
        Assert.IsTrue(model.IsReadyToRestart);
        Assert.IsTrue(model.RestartCommand.CanExecute(null));
    }

    /// <summary>无更新不允许下载。</summary>
    [TestMethod]
    public async Task NoUpdate_DisablesDownload()
    {
        using var model = Create(new UpdateStub { Release = null });
        await model.CheckOnStartupAsync();
        Assert.IsNull(model.AvailableRelease);
        Assert.IsFalse(model.DownloadCommand.CanExecute(null));
        StringAssert.Contains(model.Message, "最新");
    }

    /// <summary>明文、凭据和查询参数不能用作包更新源。</summary>
    [TestMethod]
    [DataRow("http://server/updates/")]
    [DataRow("https://user:password@server/updates/")]
    [DataRow("https://server/updates/?token=secret")]
    [DataRow("")]
    public void InvalidFeed_IsNotConfigured(string url)
    {
        var service = new VelopackUpdateService(Options.Create(new ClientUpdateOptions { FeedUrl = url }));
        Assert.IsFalse(service.IsConfigured);
        Assert.IsFalse(service.IsInstalled);
        Assert.IsFalse(string.IsNullOrWhiteSpace(service.CurrentVersion));
    }

    internal static ClientUpdateViewModel Create(UpdateStub service, RestartStub? guard = null) =>
        new(service, guard ?? new(), NullLogger<ClientUpdateViewModel>.Instance);

    /// <summary>可控的包引擎替身，支持忽略取消的迟到响应及下载中止。</summary>
    internal sealed class UpdateStub : IClientUpdateService
    {
        public string CurrentVersion { get; set; } = "0.1.0";
        public bool IsInstalled { get; set; } = true;
        public bool IsConfigured { get; set; } = true;
        public bool IsDownloaded { get; set; }
        public bool FailCheck { get; set; }
        public bool FailDownload { get; set; }
        public bool WaitForCancellation { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public int Restarts { get; private set; }
        public ClientUpdateRelease? Release { get; set; } = new("0.1.1", "更新说明");
        public Task<ClientUpdateRelease?>? CheckTask { get; set; }
        /// <inheritdoc />
        public Task<ClientUpdateRelease?> CheckAsync(CancellationToken token)
        {
            Checks++;
            if (FailCheck) throw new IOException("测试网络失败");
            return CheckTask ?? Task.FromResult(Release);
        }
        /// <inheritdoc />
        public async Task DownloadAsync(IProgress<int> progress, CancellationToken token)
        {
            Downloads++;
            if (FailDownload) throw new IOException("测试下载失败");
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token);
            progress.Report(100);
            IsDownloaded = true;
        }
        /// <inheritdoc />
        public void ApplyAndRestart() => Restarts++;
    }

    /// <summary>提供允许或拒绝重启的离开协议。</summary>
    internal sealed class RestartStub : IClientRestartGuard
    {
        public bool Allow { get; set; } = true;
        /// <inheritdoc />
        public Task<bool> CanRestartAsync(CancellationToken token) => Task.FromResult(Allow);
    }
}
