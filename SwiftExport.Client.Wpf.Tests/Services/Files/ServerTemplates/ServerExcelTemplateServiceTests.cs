using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Files;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.ServerTemplates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates;

/// <summary>验证服务器模板公共服务的在线授权、精确版本、内容缓存及最终提交边界，无需真实 API 或 STA。</summary>
[TestClass]
public sealed class ServerExcelTemplateServiceTests
{
    /// <summary>目录只返回精确契约并形成不可变快照；普通传输 DTO 的后续修改不得影响已返回结果。</summary>
    [TestMethod]
    public async Task ListAsync_ShouldFilterExactContractAndCopyMetadata()
    {
        using var fixture = new ServerTemplateTestContext();
        var other = fixture.Info("purchase-standard");
        other.DocumentType = "PurchaseOrder";
        var newer = fixture.Info("sales-new-schema");
        newer.DataContractVersion = 2;
        fixture.Api.Items.AddRange([other, newer]);
        var result = await fixture.Service.ListAsync(ServerTemplateTestContext.Contract);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("sales-standard", result[0].TemplateId);
        fixture.Api.Items[0].DisplayName = "后来修改的名称";
        Assert.AreEqual("销售单标准模板", result[0].DisplayName);
    }

    /// <summary>导出每次重新查询在线目录，缓存只减少正文下载，公开完成进度仅在发布后出现一次。</summary>
    [TestMethod]
    public async Task Export_ShouldRelistOnCacheHitAndKeepDeploymentPrefix()
    {
        using var fixture = new ServerTemplateTestContext();
        var completed = 0;
        var progress = new ServerTemplateTestContext.StageProgress(value =>
        {
            if (value.Stage == "Completed")
            {
                ++completed;
                Assert.IsTrue(File.Exists(fixture.Output(completed == 1 ? "first" : "second")));
            }
        });
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("first"), progress: progress);
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("second"), progress: progress);
        Assert.AreEqual(2, fixture.Api.Calls);
        Assert.AreEqual(1, fixture.Factory.Handler.Calls);
        Assert.AreEqual(2, fixture.Renderer.Calls);
        Assert.AreEqual(2, completed);
        Assert.AreEqual("https://server-a.example/prefix/api/export-templates/sales-standard/versions/1/content",
            fixture.Factory.Handler.Requests.Single().AbsoluteUri);
    }

    /// <summary>目录没有精确版本或契约不匹配时拒绝；已有缓存不允许自动升级或绕过停用。</summary>
    [TestMethod]
    public async Task Export_ShouldRejectRetiredVersionAndIncompatibleContractBeforeGeneration()
    {
        using var fixture = new ServerTemplateTestContext();
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("initial"));
        fixture.Api.Items[0].TemplateVersion = 2;
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("retired")));
        Assert.AreEqual("TemplateVersionUnavailable", exception.Code);
        fixture.Api.Items[0].TemplateVersion = 1;
        var mismatch = fixture.Request() with { Contract = ServerTemplateTestContext.Contract with { DataContractVersion = 2 } };
        exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(mismatch, fixture.Output("mismatch")));
        Assert.AreEqual("TemplateVersionUnavailable", exception.Code);
        Assert.AreEqual(1, fixture.Renderer.Calls);
        Assert.AreEqual(1, fixture.Factory.Handler.Calls);
        Assert.IsFalse(File.Exists(fixture.Output("retired")));
    }

    /// <summary>损坏缓存每次通过真实文件长度与散列检查发现，再下载完整文件恢复。</summary>
    [TestMethod]
    public async Task Export_ShouldReplaceCorruptCacheWithValidatedDownload()
    {
        using var fixture = new ServerTemplateTestContext();
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("initial"));
        var cache = Directory.GetFiles(fixture.CachePath, "*.xlsx").Single();
        var corrupt = fixture.Bytes.ToArray();
        corrupt[^1] ^= 1;
        await File.WriteAllBytesAsync(cache, corrupt);
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("restored"));
        Assert.AreEqual(2, fixture.Factory.Handler.Calls);
        CollectionAssert.AreEqual(fixture.Bytes, await File.ReadAllBytesAsync(cache));
        await File.WriteAllBytesAsync(cache, fixture.Bytes[..^10]);
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("truncated"));
        Assert.AreEqual(3, fixture.Factory.Handler.Calls);
        CollectionAssert.AreEqual(fixture.Bytes, await File.ReadAllBytesAsync(cache));
    }

    /// <summary>同版本并发合并下载，首个等待者取消不能取消仍在等待的调用方。</summary>
    [TestMethod]
    public async Task Export_ShouldShareDownloadWithoutSharingCallerCancellation()
    {
        using var fixture = new ServerTemplateTestContext();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Factory.Handler.Response = async (_, token) =>
        {
            started.TrySetResult(token);
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Bytes) };
        };
        var first = fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("first"), cancellationToken: cancellation.Token);
        var downloadToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("second"),
            progress: new ServerTemplateTestContext.StageProgress(value =>
            { if (value.Stage == "WaitingForServerTemplate") secondWaiting.TrySetResult(); }));
        await secondWaiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        Assert.IsFalse(downloadToken.IsCancellationRequested);
        release.TrySetResult();
        await second;
        Assert.AreEqual(1, fixture.Factory.Handler.Calls);
        Assert.IsFalse(File.Exists(fixture.Output("first")));
        Assert.IsTrue(File.Exists(fixture.Output("second")));
    }

    /// <summary>只有一个调用方取消时共享下载也取消，不能在退出等待后继续发布缓存或损坏原目标。</summary>
    [TestMethod]
    public async Task Export_ShouldCancelDownloadWhenLastWaiterLeaves()
    {
        using var fixture = new ServerTemplateTestContext();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Factory.Handler.Response = async (_, token) =>
        {
            entered.TrySetResult(token);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.TrySetResult(); }
            throw new InvalidOperationException("取消应结束下载等待。");
        };
        await File.WriteAllBytesAsync(fixture.Output(), [7, 8, 9]);
        var pending = fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output(), overwrite: true,
            cancellationToken: cancellation.Token);
        var downloadToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(downloadToken.IsCancellationRequested);
        Assert.IsFalse(Directory.Exists(fixture.CachePath));
        CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(fixture.Output()));
        Assert.AreEqual(0, fixture.Renderer.Calls);
    }

    /// <summary>发布版本改变创建独立缓存项；配额回收最久未使用版本，不回收最近命中项。</summary>
    [TestMethod]
    public async Task Export_ShouldKeepVersionKeysAndEvictLeastRecentlyUsedContent()
    {
        var clock = new ServerTemplateTestContext.ManualTimeProvider();
        using var fixture = new ServerTemplateTestContext(options => options with { TimeProvider = clock });
        // 同一文件的不同模板版本具有不同缓存键，直接测试缓存小配额避免创建大文件。
        var cache = new ServerTemplateCache(new ServerTemplateCacheOptions
        { CacheRootPath = fixture.CachePath, MaxCacheBytes = fixture.Bytes.Length * 2L, TimeProvider = clock });
        var one = Snapshot(fixture.Info(version: 1));
        var two = Snapshot(fixture.Info(version: 2));
        var three = Snapshot(fixture.Info(version: 3));
        var keys = new[] { one, two, three }.Select(item => ServerTemplateCache.Key("server", "user", item)).ToArray();
        await cache.StoreAsync(keys[0], fixture.Bytes, action => action(), CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(1);
        await cache.StoreAsync(keys[1], fixture.Bytes, action => action(), CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.IsNotNull(await cache.ReadAsync(keys[0], one, CancellationToken.None));
        clock.Now = clock.Now.AddMinutes(1);
        await cache.StoreAsync(keys[2], fixture.Bytes, action => action(), CancellationToken.None);
        Assert.IsNotNull(await cache.ReadAsync(keys[0], one, CancellationToken.None));
        Assert.IsNull(await cache.ReadAsync(keys[1], two, CancellationToken.None));
        Assert.IsNotNull(await cache.ReadAsync(keys[2], three, CancellationToken.None));
        Assert.IsTrue(Directory.GetFiles(fixture.CachePath).Sum(path => new FileInfo(path).Length) <= fixture.Bytes.Length * 2L);
    }

    /// <summary>截断、内容散列错误、无效 ZIP 和宏容器都不得进入生成器或发布缓存。</summary>
    [TestMethod]
    public async Task Export_ShouldRejectIncompleteTamperedInvalidAndMacroContent()
    {
        using var fixture = new ServerTemplateTestContext();
        var valid = fixture.Bytes;
        var macro = Macro(valid);
        var cases = new[]
        {
            (Bytes: valid[..^10], Metadata: valid, Code: "TemplateLengthMismatch"),
            (Bytes: Enumerable.Repeat((byte)0, valid.Length).ToArray(), Metadata: valid, Code: "TemplateHashMismatch"),
            (Bytes: new byte[] { 1, 2, 3, 4 }, Metadata: new byte[] { 1, 2, 3, 4 }, Code: "InvalidXlsx"),
            (Bytes: macro, Metadata: macro, Code: "UnsupportedFileType"),
        };
        for (var index = 0; index < cases.Length; ++index)
        {
            var item = cases[index];
            fixture.Api.Items[0] = fixture.Info(version: index + 1, bytes: item.Metadata);
            fixture.Factory.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(item.Bytes) });
            var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
                fixture.Service.ExportToFileAsync(fixture.Request(version: index + 1), fixture.Output("invalid" + index)));
            Assert.AreEqual(item.Code, exception.Code);
        }
        Assert.AreEqual(0, fixture.Renderer.Calls);
        Assert.IsFalse(Directory.Exists(fixture.CachePath));
    }

    /// <summary>无 Content-Length 的响应仍受真实读取字节上限保护，不能以合法目录长度绕过。</summary>
    [TestMethod]
    public async Task Export_ShouldBoundUnknownLengthResponseBody()
    {
        using var baseline = new ServerTemplateTestContext();
        using var fixture = new ServerTemplateTestContext(options => options with
        { ReadLimits = FileReadLimits.Default with { MaxInputBytes = baseline.Bytes.Length + 1 } });
        fixture.Factory.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ServerTemplateTestContext.UnknownLengthContent(fixture.Bytes.Concat(fixture.Bytes).ToArray()) });
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output()));
        Assert.AreEqual("InputTooLarge", exception.Code);
        Assert.AreEqual(0, fixture.Renderer.Calls);
        Assert.IsFalse(File.Exists(fixture.Output()));
    }

    /// <summary>有效散列也不能绕过声明解压总量限制。</summary>
    [TestMethod]
    public async Task Export_ShouldRejectExpandedWorkbookLimit()
    {
        using var fixture = new ServerTemplateTestContext(options => options with
        { ReadLimits = FileReadLimits.Default with { MaxExpandedBytes = 64 } });
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output()));
        Assert.AreEqual("ExpandedInputTooLarge", exception.Code);
        Assert.AreEqual(0, fixture.Renderer.Calls);
        Assert.IsFalse(Directory.Exists(fixture.CachePath));
    }

    /// <summary>已有缓存超过缩小配额时，普通命中也会维护总量，不必等待新版本下载。</summary>
    [TestMethod]
    public async Task Cache_ShouldTrimPreviouslyOversizedDirectoryOnRead()
    {
        using var fixture = new ServerTemplateTestContext();
        var large = new ServerTemplateCache(new ServerTemplateCacheOptions
        { CacheRootPath = fixture.CachePath, MaxCacheBytes = fixture.Bytes.Length * 4L });
        var templates = Enumerable.Range(1, 3).Select(version => Snapshot(fixture.Info(version: version))).ToArray();
        var keys = templates.Select(item => ServerTemplateCache.Key("server", "user", item)).ToArray();
        foreach (var key in keys) await large.StoreAsync(key, fixture.Bytes, action => action(), CancellationToken.None);
        Assert.AreEqual(3, Directory.GetFiles(fixture.CachePath, "*.xlsx").Length);
        var small = new ServerTemplateCache(new ServerTemplateCacheOptions
        { CacheRootPath = fixture.CachePath, MaxCacheBytes = fixture.Bytes.Length * 2L });
        Assert.IsNotNull(await small.ReadAsync(keys[2], templates[2], CancellationToken.None));
        Assert.AreEqual(2, Directory.GetFiles(fixture.CachePath, "*.xlsx").Length);
        Assert.IsTrue(Directory.GetFiles(fixture.CachePath).Sum(path => new FileInfo(path).Length) <= fixture.Bytes.Length * 2L);
    }

    /// <summary>正文认证、权限、停用及冲突状态被分类，失败不发布用户文件或缓存。</summary>
    [TestMethod]
    [DataRow(401, "TemplateUnauthorized")]
    [DataRow(403, "TemplateForbidden")]
    [DataRow(404, "TemplateVersionUnavailable")]
    [DataRow(409, "TemplateVersionConflict")]
    public async Task Export_ShouldClassifyProtectedContentFailures(int status, string code)
    {
        using var fixture = new ServerTemplateTestContext();
        fixture.Factory.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output()));
        Assert.AreEqual(code, exception.Code);
        Assert.AreEqual(0, fixture.Renderer.Calls);
        Assert.IsFalse(Directory.Exists(fixture.CachePath));
    }

    /// <summary>有缓存时在线目录失败也明确失败，禁止回退为离线授权。</summary>
    [TestMethod]
    public async Task Export_ShouldNeverFallbackToCachedContentWhenCatalogFails()
    {
        using var fixture = new ServerTemplateTestContext();
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("initial"));
        fixture.Api.Response = _ => Task.FromResult(ServiceResult<List<ExportTemplateInfoDto>>.Fail("无法使用"));
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("offline")));
        Assert.AreEqual("TemplateCatalogFailed", exception.Code);
        Assert.AreEqual(1, fixture.Renderer.Calls);
        Assert.IsFalse(File.Exists(fixture.Output("offline")));
    }

    /// <summary>新账号及新服务器各自重新下载；同路径模板不可跨环境或身份误命中。</summary>
    [TestMethod]
    public async Task Export_ShouldIsolateUsersAndServers()
    {
        using var fixture = new ServerTemplateTestContext();
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("a"));
        fixture.User.SetCurrentUser(new MeDto { UserId = "user-b", Permissions = [] });
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("b"));
        fixture.Factory.Address = new Uri("https://server-b.example/prefix/api/export-templates/");
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("server-b"));
        Assert.AreEqual(3, fixture.Factory.Handler.Calls);
        Assert.AreEqual(3, Directory.GetFiles(fixture.CachePath, "*.xlsx").Length);
    }

    /// <summary>服务器切换后的新调用重新解析目录 API，而不是继续持有启动时的旧实例。</summary>
    [TestMethod]
    public async Task List_ShouldResolveApiForCurrentServerOnEveryOperation()
    {
        using var fixture = new ServerTemplateTestContext();
        var next = new ServerTemplateTestContext.CatalogApi();
        next.Items.Add(fixture.Info("second-server-template"));
        fixture.ApiFactory = () => fixture.Factory.Address.Host == "server-a.example" ? fixture.Api : next;
        var first = await fixture.Service.ListAsync(ServerTemplateTestContext.Contract);
        fixture.Factory.Address = new Uri("https://server-b.example/api/export-templates/");
        var second = await fixture.Service.ListAsync(ServerTemplateTestContext.Contract);
        Assert.AreEqual("sales-standard", first.Single().TemplateId);
        Assert.AreEqual("second-server-template", second.Single().TemplateId);
        Assert.AreEqual(1, fixture.Api.Calls);
        Assert.AreEqual(1, next.Calls);
    }

    /// <summary>目录即使忽略取消并迟到返回，在 ClearSession 或服务器变更后也不得继续下载或生成。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_ShouldRejectLateCatalogFromPreviousIdentityOrServer(bool serverSwitch)
    {
        using var fixture = new ServerTemplateTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.Response = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return ServiceResult<List<ExportTemplateInfoDto>>.Ok(fixture.Api.Items.ToList());
        };
        var pending = fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (serverSwitch) fixture.Factory.Address = new Uri("https://server-b.example/api/export-templates/");
        else fixture.Session.ClearSession();
        release.TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(0, fixture.Factory.Handler.Calls);
        Assert.AreEqual(0, fixture.Renderer.Calls);
        Assert.IsFalse(File.Exists(fixture.Output()));
    }

    /// <summary>忽略取消的迟到下载响应也不得发布旧会话缓存；服务器变化同样在响应接纳时检查。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Export_ShouldRejectLateDownloadBeforeCachePublication(bool serverSwitch)
    {
        using var fixture = new ServerTemplateTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Factory.Handler.Response = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ServerTemplateTestContext.ObservedContent(fixture.Bytes, () => disposed.TrySetResult()) };
        };
        var pending = fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (serverSwitch) fixture.Factory.Address = new Uri("https://server-b.example/api/export-templates/");
        else fixture.Session.ClearSession();
        release.TrySetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(Directory.Exists(fixture.CachePath));
        Assert.IsFalse(File.Exists(fixture.Output()));
        Assert.AreEqual(0, fixture.Renderer.Calls);
    }

    /// <summary>最终提交前发生取消、会话结束、账号或服务器切换时保留原目标；发布后取消仍成功。</summary>
    [TestMethod]
    [DataRow("cancel")]
    [DataRow("session")]
    [DataRow("user")]
    [DataRow("server")]
    public async Task Export_ShouldGuardAtomicOutputPublication(string action)
    {
        using var fixture = new ServerTemplateTestContext();
        using var cancellation = new CancellationTokenSource();
        var original = new byte[] { 7, 8, 9 };
        await File.WriteAllBytesAsync(fixture.Output(), original);
        var progress = new ServerTemplateTestContext.StageProgress(value =>
        {
            if (value.Stage != "PublishingOutput") return;
            switch (action)
            {
                case "cancel": cancellation.Cancel(); break;
                case "session": fixture.Session.EndSession(SessionEndReason.Unauthorized); break;
                case "user": fixture.User.SetCurrentUser(new MeDto { UserId = "user-b", Permissions = [] }); break;
                case "server": fixture.Factory.Address = new Uri("https://other.example/api/export-templates/"); break;
            }
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Service.ExportToFileAsync(
            fixture.Request(), fixture.Output(), overwrite: true, progress, cancellation.Token));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(fixture.Output()));
        Assert.AreEqual(1, Directory.GetFiles(fixture.Root).Length);
    }

    /// <summary>忽略取消的生成器完成后必须再次验证会话，且最终提交之后的取消不误报失败。</summary>
    [TestMethod]
    public async Task Export_ShouldRejectStaleRendererAndAcceptCancellationAfterCommit()
    {
        using var fixture = new ServerTemplateTestContext();
        fixture.Renderer.BeforeWrite = _ =>
        {
            fixture.User.SetCurrentUser(new MeDto { UserId = "user-b", Permissions = [] });
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("stale")));
        Assert.IsFalse(File.Exists(fixture.Output("stale")));
        fixture.Renderer.BeforeWrite = null;
        using var cancellation = new CancellationTokenSource();
        await fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output("success"),
            progress: new ServerTemplateTestContext.StageProgress(value =>
            { if (value.Stage == "Completed") cancellation.Cancel(); }), cancellationToken: cancellation.Token);
        Assert.IsTrue(File.Exists(fixture.Output("success")));
    }

    /// <summary>非法选择、契约或预先取消在网络前失败；默认覆盖保护同样不浪费下载。</summary>
    [TestMethod]
    public async Task Export_ShouldValidateBeforeNetworkAndPreserveExistingTarget()
    {
        using var fixture = new ServerTemplateTestContext();
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request() with { TemplateId = "../escape" }, fixture.Output()));
        Assert.AreEqual("InvalidTemplateSelection", exception.Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output(), cancellationToken: cancellation.Token));
        await File.WriteAllBytesAsync(fixture.Output(), [1]);
        exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            fixture.Service.ExportToFileAsync(fixture.Request(), fixture.Output()));
        Assert.AreEqual("OutputAlreadyExists", exception.Code);
        Assert.AreEqual(0, fixture.Api.Calls);
        Assert.AreEqual(0, fixture.Factory.Handler.Calls);
    }

    /// <summary>把测试目录项转成公开快照，仅用于调用实际缓存边界，不复制缓存算法。</summary>
    private static ServerExcelTemplateInfo Snapshot(ExportTemplateInfoDto item) => new(item.TemplateId, item.DisplayName,
        item.DocumentType, item.DataContractKey, item.DataContractVersion, item.TemplateVersion, item.FileLength, item.Sha256);

    /// <summary>修改真实 XLSX 的声明类型构造宏输入，保留其余字节由真实容器检查拒绝。</summary>
    private static byte[] Macro(byte[] bytes)
    {
        using var result = new MemoryStream();
        result.Write(bytes);
        result.Position = 0;
        using (var zip = new ZipArchive(result, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("[Content_Types].xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) xml = reader.ReadToEnd();
            using var output = entry.Open();
            output.SetLength(0);
            using var writer = new StreamWriter(output, new UTF8Encoding(false));
            writer.Write(xml.Replace("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
                "application/vnd.ms-excel.sheet.macroEnabled.main+xml", StringComparison.Ordinal));
        }
        return result.ToArray();
    }
}
