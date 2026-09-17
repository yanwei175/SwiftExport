using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.Services;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>真实详情 ViewModel 的过账交互测试，不访问后端或展示真实弹窗。</summary>
[TestClass]
public sealed class PackingListPostingViewModelTests
{
    /// <summary>干净草稿保存失败也必须停止，不允许沿用旧版本预览。</summary>
    [TestMethod]
    public async Task FailedSaveStopsPreview()
    {
        var f = new Fixture(); using var vm = f.Create(); await f.Load(vm);
        await vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Api.Updates.Count); Assert.AreEqual(0, f.Dialogs.InputCount);
    }

    /// <summary>网络结果不确定后冻结编辑、关闭和刷新，重试复用原 CommandId，不再保存或预览。</summary>
    [TestMethod]
    public async Task UnknownResultRetriesSameCommand()
    {
        var f = new Fixture();
        f.Api.Update = (_, _) => { f.Detail.Header.Version++; return Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Ok(new() { Uid = f.Detail.Header.Uid, Detail = f.Detail })); };
        var commands = new List<Guid>();
        f.Api.Preview = (uid, request) => Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Ok(new()
        { PackingListId = uid, ExpectedVersion = request.ExpectedVersion, BusinessDate = request.BusinessDate, PreviewToken = "token" }));
        f.Api.Post = (_, request) =>
        {
            commands.Add(request.CommandId);
            if (commands.Count == 1) return Task.FromResult(ServiceResult<PackingListPostingCommandResult>.Fail("连接中断"));
            f.Detail.Header.PostingStatus = PackingListPostingStatusEnum.Posted; f.Detail.Header.Version++;
            return Task.FromResult(ServiceResult<PackingListPostingCommandResult>.Ok(new() { Status = "AlreadyApplied", Detail = f.Detail }));
        };
        using var vm = f.Create(); await f.Load(vm);
        await vm.PostCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsEditable); Assert.IsFalse(await vm.CanCloseAsync(default));
        await vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(2, commands.Count); Assert.AreEqual(commands[0], commands[1]);
        Assert.AreEqual(1, f.Api.Updates.Count); Assert.AreEqual(1, f.Dialogs.InputCount);
        Assert.IsTrue(vm.IsReadOnly); Assert.IsTrue(await vm.CanCloseAsync(default));
    }

    /// <summary>用户取消或预览存在阻断时不发送过账，即使弹窗替身错误返回确认。</summary>
    [TestMethod]
    [DataRow("cancel")]
    [DataRow("blocker")]
    [DataRow("permission")]
    [DataRow("identity")]
    public async Task InvalidOrCancelledPreviewNeverPosts(string scenario)
    {
        var f = new Fixture(); f.EnableSaveAndPreview();
        var posts = 0;
        f.Api.Post = (_, _) => { posts++; throw new AssertFailedException("不可提交。"); };
        f.Dialogs.Confirm = preview =>
        {
            if (scenario == "permission") f.User.Clear();
            return scenario != "cancel";
        };
        f.Api.Preview = (uid, request) => Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Ok(new() {
            PackingListId = scenario == "identity" ? Guid.NewGuid() : uid, ExpectedVersion = request.ExpectedVersion,
            BusinessDate = request.BusinessDate, PreviewToken = "token",
            Blockers = scenario == "blocker" ? [new() { Code = "SHORTFALL", Message = "库存不足" }] : [] }));
        using var vm = f.Create(); await f.Load(vm);
        await vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(0, posts);
        Assert.IsTrue(await vm.CanCloseAsync(default));
    }

    /// <summary>服务端成功标记与详情状态矛盾时不能清除不确定命令或恢复编辑。</summary>
    [TestMethod]
    public async Task InconsistentSuccessKeepsCommandForRetry()
    {
        var f = new Fixture(); f.EnableSaveAndPreview();
        var commands = new List<Guid>();
        f.Api.Post = (_, request) => { commands.Add(request.CommandId); return Task.FromResult(
            ServiceResult<PackingListPostingCommandResult>.Ok(new() { Status = "Success", Detail = f.Detail })); };
        using var vm = f.Create(); await f.Load(vm);
        await vm.PostCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsEditable); Assert.IsFalse(await vm.CanCloseAsync(default));
        await vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(2, commands.Count); Assert.AreEqual(commands[0], commands[1]);
        Assert.AreEqual(1, f.Api.Updates.Count);
    }

    /// <summary>反向网络结果不确定时保持原原因、日期和命令，不重新保存已过账单据。</summary>
    [TestMethod]
    public async Task ReverseUnknownResultRetriesOriginalRequestWithoutSaving()
    {
        var f = new Fixture(); f.Detail.Header.PostingStatus = PackingListPostingStatusEnum.Posted;
        var requests = new List<ReversePackingListCommand>();
        f.Api.ReversePreview = (uid, request) => Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Ok(new() {
            PackingListId = uid, ExpectedVersion = request.ExpectedVersion, BusinessDate = request.BusinessDate,
            PreviewToken = "reverse-token", IsReverse = true }));
        f.Api.Reverse = (_, request) =>
        {
            requests.Add(request);
            if (requests.Count == 1) return Task.FromResult(ServiceResult<PackingListPostingCommandResult>.Fail("响应中断"));
            f.Detail.Header.PostingStatus = PackingListPostingStatusEnum.Reversed; f.Detail.Header.Version++;
            return Task.FromResult(ServiceResult<PackingListPostingCommandResult>.Ok(new() { Status = "AlreadyApplied", Detail = f.Detail }));
        };
        using var vm = f.Create(); await f.Load(vm);
        await vm.ReverseCommand.ExecuteAsync(null);
        Assert.IsFalse(await vm.CanCloseAsync(default));
        await vm.ReverseCommand.ExecuteAsync(null);
        Assert.AreEqual(2, requests.Count); Assert.AreSame(requests[0], requests[1]);
        Assert.AreEqual("录单错误", requests[0].Reason);
        Assert.AreEqual(0, f.Api.Updates.Count); Assert.AreEqual(1, f.Dialogs.InputCount);
        Assert.AreEqual(PackingListPostingStatusEnum.Reversed, vm.Header.PostingStatus);
        Assert.IsTrue(await vm.CanCloseAsync(default)); Assert.IsFalse(vm.PostCommand.CanExecute(null));
    }

    /// <summary>最小已保存草稿与真实会话权限。</summary>
    private sealed class Fixture
    {
        public PackingListApiStub Api { get; } = new();
        public PostingDialogs Dialogs { get; } = new();
        public CurrentUserService User { get; } = new();
        public PackingListDetailDto Detail { get; } = new()
        { Header = new() { Uid = Guid.NewGuid(), Version = 1, CustomerId = Guid.NewGuid(), CustomerNameSnapshot = "customer", InvoiceDate = DateTime.Today } };
        public Fixture()
        {
            Detail.Lines.Add(new() { Uid = Guid.NewGuid(), PackingListHeadId = Detail.Header.Uid, Version = 1, LineNumber = 1,
                GrossDemandId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Qty = 2 });
            Api.Get = _ => Task.FromResult(ServiceResult<PackingListDetailDto>.Ok(Detail));
        }
        public PackingListDetailViewModel Create()
        {
            User.SetCurrentUser(new() { Permissions = [PermissionKeys.ModuleShippingAccess, PermissionKeys.PackingListPost, PermissionKeys.PackingListReverse] });
            var ui = new PackingListUiStub();
            return new(Api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), new ReferenceDataCacheServiceStub(), new PackingListSourceStub(),
                new PackingListNavigationStub(), ui, ui, Dialogs, User);
        }
        /// <summary>提供明确的保存确认和匹配版本预览，其余测试可覆盖具体失败。</summary>
        public void EnableSaveAndPreview()
        {
            Api.Update = (_, _) => { Detail.Header.Version++; return Task.FromResult(ServiceResult<PackingListDraftCommandResult>.Ok(new() { Uid = Detail.Header.Uid, Detail = Detail })); };
            Api.Preview = (uid, request) => Task.FromResult(ServiceResult<PackingListPostingPreviewDto>.Ok(new()
                { PackingListId = uid, ExpectedVersion = request.ExpectedVersion, BusinessDate = request.BusinessDate, PreviewToken = "token" }));
        }
        public Task Load(PackingListDetailViewModel vm) => vm.InitializeDocumentAsync(new PackingListOpenRequest { Uid = Detail.Header.Uid }, default);
    }

    /// <summary>明确确认的弹窗替身，记录输入次数以验证重试不重新预览。</summary>
    private sealed class PostingDialogs : IPackingListPostingDialogService
    {
        public int InputCount { get; private set; }
        public Func<PackingListPostingPreviewDto, bool> Confirm { get; set; } = _ => true;
        public Task<PackingListPostingOptions?> GetOptionsAsync(bool reverse, CancellationToken token)
        { InputCount++; return Task.FromResult<PackingListPostingOptions?>(new() { IsReverse = reverse, Reason = "录单错误" }); }
        public Task<bool> ConfirmAsync(PackingListPostingPreviewDto preview, CancellationToken token) => Task.FromResult(Confirm(preview));
        public Task ShowTraceAsync(PackingListTraceDto trace, CancellationToken token) => Task.CompletedTask;
    }
}
