using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证独立供应商反馈入口保留追加纠错命令，并保护尚未提交的原始输入。</summary>
[TestClass]
public sealed class SupplierFeedbackEntryTests
{
    /// <summary>数量下降发出有版本、身份和原因的追加命令，刷新后原反馈和纠错同时保留。</summary>
    [TestMethod]
    public async Task Correction_UsesExistingReportCommandAndPreservesHistory()
    {
        using var f = new Fixture(); await f.OpenAsync();
        f.Vm.ReportReadyQtyInput = "4"; f.Vm.ReportNote = "实物复核"; f.Vm.CorrectionReason = "原反馈多计";
        await f.Vm.ReportProgressCommand.ExecuteAsync(null);
        var command = f.Api.Commands.Single();
        Assert.AreNotEqual(Guid.Empty, command.CommandId); Assert.AreEqual(f.Api.Line.PurchaseOrderLineId, command.PurchaseOrderLineId);
        Assert.AreEqual(7, command.ExpectedPurchaseOrderVersion); Assert.AreEqual(3, command.ExpectedLineVersion);
        Assert.AreEqual(4m, command.ReadyQty); Assert.AreEqual("原反馈多计", command.CorrectionReason);
        Assert.AreEqual(2, f.Vm.SelectedLine!.ProgressHistory.Count); Assert.IsTrue(f.Vm.SelectedLine.ProgressHistory[0].IsCorrection);
        Assert.AreEqual(8m, f.Vm.SelectedLine.ProgressHistory[1].ReadyQty); Assert.IsFalse(f.Vm.HasUnsavedChanges);
        Assert.AreEqual("供应商反馈 · PO-FEEDBACK", f.Vm.DocumentTitle);
    }

    /// <summary>原始无效输入也是未提交数据，关闭须明确放弃，查询和翻页不能覆盖它。</summary>
    [TestMethod]
    public async Task DirtyInput_BlocksSelectionAndRequiresExplicitDiscard()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.IsDrawerOpen = true; f.Vm.ReportReadyQtyInput = "abc";
        Assert.IsFalse(f.Vm.CanChangeSelection); Assert.IsTrue(f.Vm.HasUnsavedChanges);
        f.Dialogs.Answer = false; Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        await f.Vm.CloseDrawerCommand.ExecuteAsync(null); Assert.IsTrue(f.Vm.IsDrawerOpen);
        await f.Vm.SearchCommand.ExecuteAsync(null); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Api.SearchCount); Assert.AreEqual(1, f.Api.DetailCount); Assert.AreEqual("abc", f.Vm.ReportReadyQtyInput);
        await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Api.Commands.Count);
        f.Dialogs.Answer = true; await f.Vm.CloseDrawerCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.IsDrawerOpen); Assert.IsFalse(f.Vm.HasUnsavedChanges); Assert.IsTrue(f.Vm.CanChangeSelection);
    }

    /// <summary>确认期间保持Busy，不能关闭或二次提交；确认前冻结的输入不会被等待期间的修改替换。</summary>
    [TestMethod]
    public async Task BusyConfirmation_FreezesCommandAndBlocksClose()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "确认前备注";
        var answer = new TaskCompletionSource<bool>(); f.Dialogs.Pending = answer;
        var reporting = f.Vm.ReportProgressCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.IsBusy); Assert.IsFalse(f.Vm.CanChangeSelection); Assert.IsFalse(f.Vm.ReportProgressCommand.CanExecute(null));
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        f.Vm.ReportNote = "等待期间修改"; answer.SetResult(true); await reporting;
        Assert.AreEqual("确认前备注", f.Api.Commands.Single().Note); Assert.IsFalse(f.Vm.IsBusy);
    }

    /// <summary>服务端可登记标记不能替代当前会话权限，确认中撤权或换用户后旧命令不能发出。</summary>
    [TestMethod]
    public async Task CurrentPermissionAndSession_AreRecheckedAfterConfirmation()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "新反馈";
        f.User.SetCurrentUser(new() { UserId = "u", Permissions = [] });
        Assert.IsFalse(f.Vm.CanReportSelectedOrder); await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Api.Commands.Count);
        f.User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementProgressView, PermissionKeys.ProcurementProgressReport] });
        // 撤权已失效旧依据，原账号恢复后须明确放弃输入并重读，不能沿用撤权前的登记资格。
        await f.Vm.CloseDrawerCommand.ExecuteAsync(null); await f.Vm.RefreshCommand.ExecuteAsync(null); f.Vm.ReportNote = "新反馈";
        var answer = new TaskCompletionSource<bool>(); f.Dialogs.Pending = answer;
        var reporting = f.Vm.ReportProgressCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new() { UserId = "other", Permissions = [PermissionKeys.ProcurementProgressReport] });
        answer.SetResult(true); await reporting; Assert.AreEqual(0, f.Api.Commands.Count); Assert.AreEqual("新反馈", f.Vm.ReportNote);
    }

    /// <summary>关闭页后迟到详情不恢复旧数据，也不再允许打开输入抽屉。</summary>
    [TestMethod]
    public async Task DisposedEntry_IgnoresLateDetails()
    {
        using var f = new Fixture(); var detail = new TaskCompletionSource<ServiceResult<PurchaseOrderProgressDetailDto>>(); f.Api.PendingDetail = detail.Task;
        var loading = f.OpenAsync(); f.Vm.Dispose(); detail.SetResult(f.Api.Detail()); await loading;
        Assert.AreEqual(0, f.Vm.Lines.Count); Assert.IsFalse(f.Vm.OpenDrawerCommand.CanExecute(null));
    }

    private sealed class Fixture : IDisposable
    {
        public Api Api { get; } = new(); public Dialogs Dialogs { get; } = new(); public User User { get; } = new();
        public PurchaseOrderProgressWorkspaceViewModel Vm { get; }
        public Fixture() => Vm = new(Api, new Runner(), Dialogs, new Messages(), User);
        /// <summary>写入用例从订单文档进入，Shell 选单入口只读。</summary>
        public Task OpenAsync() => Vm.InitializeDocumentAsync(new PurchaseOrderProgressOpenRequest { PurchaseOrderId = Api.OrderId }, CancellationToken.None);
        public void Dispose() => Vm.Dispose();
    }
    private sealed class Api : IPurchaseOrderProgressApi
    {
        private readonly Guid _order = Guid.NewGuid();
        public Guid OrderId => _order;
        public PurchaseOrderProgressLineDto Line { get; } = new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 3, LineNumber = 1, Sku = "SKU", Quantity = 10,
            ProgressHistory = [new() { Sequence = 1, ReadyQty = 8 }] };
        public int SearchCount; public int DetailCount; public List<ReportPurchaseOrderProgressCommand> Commands { get; } = [];
        public Task<ServiceResult<PurchaseOrderProgressDetailDto>>? PendingDetail;
        public Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(PurchaseOrderProgressSearchRequest request, CancellationToken cancellationToken = default)
        { SearchCount++; return Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>.Ok(new() { Items = [new() { PurchaseOrderId = _order, Version = 7, CanReportProgress = true }], TotalCount = 1, PageIndex = 1, PageSize = 20 })); }
        public ServiceResult<PurchaseOrderProgressDetailDto> Detail() => ServiceResult<PurchaseOrderProgressDetailDto>.Ok(new()
        { PurchaseOrderId = _order, Version = 7, OrderNo = "PO-FEEDBACK", CanReportProgress = true,
            ConfirmationStatus = ConfirmationStatusEnum.已确认, LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active, Lines = [Line] });
        public Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
        { DetailCount++; return PendingDetail ?? Task.FromResult(Detail()); }
        public Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(Guid purchaseOrderId, ReportPurchaseOrderProgressCommand command, CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(_order, purchaseOrderId); Commands.Add(command);
            Line.ProgressHistory.Add(new() { Sequence = 2, ReadyQty = command.ReadyQty ?? 8, Note = command.Note, IsCorrection = command.ReadyQty < 8, CorrectionReason = command.CorrectionReason });
            // 追加反馈不推进采购头版本，回执遵守真实服务的当前版本语义。
            return Task.FromResult(ServiceResult<PurchaseOrderProgressReportResult>.Ok(new() { PurchaseOrderId = _order, Version = 7, SupplierProgressReportId = Guid.NewGuid() }));
        }
    }
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    private sealed class Dialogs : IDialogService
    {
        public bool Answer = true; public TaskCompletionSource<bool>? Pending;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Pending?.Task ?? Task.FromResult(Answer);
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Messages : IMessageService
    {
        public void ShowInfo(string title, string message) { } public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { } public void ShowError(string title, string message) { }
    }
    private sealed class User : ICurrentUserService
    {
        public event EventHandler? CurrentUserChanged;
        public MeDto? CurrentUser { get; private set; } = new() { UserId = "u", Permissions = [PermissionKeys.ProcurementProgressView, PermissionKeys.ProcurementProgressReport] };
        public bool IsAuthenticated => CurrentUser is not null; public string DisplayName => "录入测试"; public bool IsAdmin => false;
        public IReadOnlyList<string> Roles => []; public IReadOnlyList<string> Permissions => CurrentUser?.Permissions ?? [];
        public bool HasPermission(string key) => Permissions.Contains(key);
        public void SetCurrentUser(MeDto user) { CurrentUser = user; CurrentUserChanged?.Invoke(this, EventArgs.Empty); }
        public void Clear() { CurrentUser = null; CurrentUserChanged?.Invoke(this, EventArgs.Empty); }
    }
}
