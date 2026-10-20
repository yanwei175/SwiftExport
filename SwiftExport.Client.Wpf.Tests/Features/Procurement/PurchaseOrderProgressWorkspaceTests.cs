using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>离线验证反馈原命令重试、读取恢复、身份隔离与多个入口的编辑保护。</summary>
[TestClass]
[DoNotParallelize]
public sealed partial class PurchaseOrderProgressWorkspaceTests
{
    /// <summary>未知后所有字段与 CommandId 原样重试；成功即清输入，后续读失败只允许恢复读取。</summary>
    [TestMethod]
    public async Task Unknown_retries_exact_payload_and_success_refresh_failure_never_reports_again()
    {
        using var f = new Fixture(); await f.OpenAsync();
        f.Vm.ReportReadyQtyInput = "2.5"; f.Vm.ReportExpectedReadyDate = new(2026, 11, 2); f.Vm.ReportNote = "原备注";
        f.Report = command => f.Writes.Count == 1 ? Failure(ServiceErrorCodes.OutcomeUnknown) : f.Success(command);
        await f.Vm.ReportProgressCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasPendingReport); Assert.IsFalse(f.Vm.CanEditReport);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        Assert.IsFalse(f.Vm.RefreshCommand.CanExecute(null));
        // 即使程序性修改显示副本，也不能改变已冻结的写入负载。
        f.Vm.ReportNote = "另一个备注"; f.Vm.ReportReadyQtyInput = "9";
        f.FailDetail = true;
        await f.Vm.RetryReportCommand.ExecuteAsync(null);
        Assert.HasCount(2, f.Writes); Assert.AreEqual(f.Writes[0], f.Writes[1]);
        Assert.IsFalse(f.Vm.HasPendingReport); Assert.IsFalse(f.Vm.HasUnsavedChanges);
        Assert.IsFalse(f.Vm.ReportProgressCommand.CanExecute(null));
        StringAssert.Contains(f.Vm.DetailStatusText, "恢复读取");
        f.FailDetail = false; await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.HasCount(2, f.Writes); Assert.IsTrue(f.Vm.CanEditReport);
    }

    /// <summary>命令忙、暂不可用与不完整成功均不得生成第二个反馈身份。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.CommandBusy)]
    [DataRow(ServiceErrorCodes.ServiceUnavailable)]
    [DataRow("INCOMPLETE_SUCCESS")]
    public async Task Uncertain_results_preserve_original_command(string code)
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "等待核实";
        f.Report = _ => code == "INCOMPLETE_SUCCESS" ? ServiceResult<PurchaseOrderProgressReportResult>.Ok(new()) : Failure(code);
        await f.Vm.ReportProgressCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasPendingReport);
        var original = f.Writes.Single();
        f.Report = f.Success; await f.Vm.RetryReportCommand.ExecuteAsync(null);
        Assert.AreEqual(original, f.Writes.Last()); Assert.IsFalse(f.Vm.HasPendingReport);
    }

    /// <summary>明确业务失败保留输入且不建立未知状态；取消确认不发送。</summary>
    [TestMethod]
    public async Task Definitive_failure_and_cancel_keep_input_without_pending_command()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "保留";
        f.Dialogs.Confirm = () => Task.FromResult(false);
        await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.HasCount(0, f.Writes);
        f.Dialogs.Confirm = () => Task.FromResult(true); f.Report = _ => Failure(ServiceErrorCodes.VersionConflict);
        await f.Vm.ReportProgressCommand.ExecuteAsync(null);
        Assert.AreEqual("保留", f.Vm.ReportNote); Assert.IsFalse(f.Vm.HasPendingReport);
    }

    /// <summary>非法原文不能退回旧数字提交；累计下降必须提供纠错原因。</summary>
    [TestMethod]
    public async Task Invalid_raw_input_and_correction_without_reason_do_not_write()
    {
        using var f = new Fixture(); f.Context.Lines[0].ProgressHistory.Add(new() { Sequence = 1, ReadyQty = 5 });
        await f.OpenAsync(); f.Vm.ReportReadyQtyInput = "3"; f.Vm.ReportReadyQtyInput = "非法数字";
        await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.HasCount(0, f.Writes); Assert.IsTrue(f.Vm.HasUnsavedChanges);
        f.Vm.ReportReadyQtyInput = "2"; await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.HasCount(0, f.Writes);
        f.Vm.CorrectionReason = "供应商纠正"; await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.HasCount(1, f.Writes);
    }

    /// <summary>确认期间冻结输入和关闭，权限变化后不得再发送已捕获命令。</summary>
    [TestMethod]
    public async Task Confirmation_freezes_interaction_and_rechecks_permission()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "待确认";
        var confirmation = new TaskCompletionSource<bool>(); f.Dialogs.Confirm = () => confirmation.Task;
        var task = f.Vm.ReportProgressCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.IsBusy); Assert.IsFalse(f.Vm.CanEditReport); Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        await f.Vm.ReportProgressCommand.ExecuteAsync(null); Assert.HasCount(0, f.Writes);
        f.SetUser("u", report: false); confirmation.SetResult(true); await task;
        Assert.HasCount(0, f.Writes); Assert.AreEqual("待确认", f.Vm.ReportNote);
    }

    /// <summary>未知反馈留在应用会话；换账号不能发送，原账号重开恢复同一完整命令。</summary>
    [TestMethod]
    public async Task Pending_command_survives_page_disposal_and_isolated_from_user_and_environment()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "原账号未决";
        f.Report = _ => Failure(ServiceErrorCodes.OutcomeUnknown); await f.Vm.ReportProgressCommand.ExecuteAsync(null);
        var original = f.Writes.Single();
        f.SetUser("other"); Assert.IsFalse(f.Vm.RetryReportCommand.CanExecute(null));
        await f.Vm.RetryReportCommand.ExecuteAsync(null); Assert.HasCount(1, f.Writes);
        Assert.IsNull(f.Session.Find("other", f.Context.PurchaseOrderId));
        Assert.IsNull(new PurchaseOrderProgressCommandSession("https://other-api/").Find("u", f.Context.PurchaseOrderId));
        f.Vm.Dispose(); f.SetUser("u");
        using var recovered = f.CreateVm(); await recovered.InitializeDocumentAsync(f.Request(), CancellationToken.None);
        Assert.AreEqual("原账号未决", recovered.ReportNote); Assert.IsTrue(recovered.HasPendingReport);
        f.Report = f.Success; await recovered.RetryReportCommand.ExecuteAsync(null);
        Assert.AreEqual(original, f.Writes.Last()); Assert.IsFalse(recovered.HasPendingReport);
    }

    /// <summary>未知后的权限拒绝继续保留原命令，不把身份变化当作未提交证明。</summary>
    [TestMethod]
    public async Task Authentication_failure_after_unknown_keeps_original_command()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "保留原反馈";
        f.Report = _ => Failure(ServiceErrorCodes.OutcomeUnknown); await f.Vm.ReportProgressCommand.ExecuteAsync(null);
        f.Report = _ => Failure(ServiceErrorCodes.Unauthenticated); await f.Vm.RetryReportCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasPendingReport); Assert.AreEqual(f.Writes[0], f.Writes[1]);
    }

    /// <summary>脏输入阻断换行、查询和页码，干净后重复入口才接纳新行。</summary>
    [TestMethod]
    public async Task Dirty_input_blocks_new_line_search_paging_and_close_cancel()
    {
        using var f = new Fixture(); await f.OpenAsync(); var line = f.Vm.SelectedLine;
        f.Vm.ReportReadyQtyInput = "abc"; f.Vm.AcceptOpenRequest(f.Request(f.Context.Lines[1].PurchaseOrderLineId));
        Assert.AreSame(line, f.Vm.SelectedLine); Assert.IsFalse(f.Vm.SearchCommand.CanExecute(null));
        Assert.IsFalse(f.Vm.PageChangeCommand.CanExecute(null)); StringAssert.Contains(f.Vm.SelectionBlockReason, "尚未提交");
        f.Dialogs.Confirm = () => Task.FromResult(false); await f.Vm.CloseDrawerCommand.ExecuteAsync(null);
        Assert.AreEqual("abc", f.Vm.ReportReadyQtyInput);
        f.Dialogs.Confirm = () => Task.FromResult(true); await f.Vm.CloseDrawerCommand.ExecuteAsync(null);
        f.Vm.AcceptOpenRequest(f.Request(f.Context.Lines[1].PurchaseOrderLineId));
        Assert.AreEqual(f.Context.Lines[1].PurchaseOrderLineId, f.Vm.SelectedLine!.Uid);
    }

    /// <summary>直达详情不依赖选单分页，缺登记权限与订单状态分别提供原因。</summary>
    [TestMethod]
    public async Task Direct_context_uses_id_and_separates_permission_from_order_state()
    {
        using var f = new Fixture(); f.SetUser("u", report: false); await f.OpenAsync();
        Assert.HasCount(0, f.Searches); Assert.AreEqual(f.Context.PurchaseOrderId, f.Vm.CurrentOrder!.PurchaseOrderId);
        StringAssert.Contains(f.Vm.FollowUpPermissionText, "登记权限"); Assert.IsTrue(f.Vm.OpenDrawerCommand.CanExecute(null));
        f.Context.CanReportProgress = false; f.Context.ReadOnlyReason = "采购单已关闭";
        f.SetUser("u"); await f.Vm.RefreshCommand.ExecuteAsync(null);
        StringAssert.Contains(f.Vm.FollowUpPermissionText, "采购单已关闭"); Assert.IsFalse(f.Vm.CanEditReport);
        f.SetUser("u", view: false); Assert.IsFalse(f.Vm.RefreshCommand.CanExecute(null)); Assert.IsNull(f.Vm.CurrentOrder);
    }

    /// <summary>Shell 从 A 换到 B 且 B 加载失败，不得使用 A 的旧依据打开反馈。</summary>
    [TestMethod]
    public async Task Picker_failed_switch_does_not_open_previous_order()
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync(); Assert.IsTrue(f.Vm.OpenDrawerCommand.CanExecute(null));
        f.Vm.SelectedOrder = new() { PurchaseOrderId = Guid.NewGuid() }; f.FailDetail = true;
        await f.Vm.LoadSelectedOrderCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.OpenDrawerCommand.CanExecute(null)); await f.Vm.OpenDrawerCommand.ExecuteAsync(null);
        Assert.HasCount(0, f.Navigation.OpenTabs);
    }

    /// <summary>Shell、管理和详情实际命令共用同一订单文档；重开不能替换脏行和未知反馈。</summary>
    [TestMethod]
    public async Task Shell_manager_and_detail_reuse_order_document_and_protect_dirty_state()
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync(); await f.Vm.OpenDrawerCommand.ExecuteAsync(null);
        var tab = f.Navigation.SelectedTab!; var document = (PurchaseOrderProgressWorkspaceViewModel)tab.ContentViewModel!;
        Assert.AreEqual(PurchaseOrderProgressNavigation.DocumentKey(f.Context.PurchaseOrderId), tab.DocumentKey);
        using var manager = new PurchaseOrderManagerViewModel(null!, null!, null!, f.Navigation, f.Dialogs, f.Runner, f.Messages, f.User);
        manager.SelectedOrder = new() { PurchaseOrderId = f.Context.PurchaseOrderId };
        document.ReportNote = "原录入";
        await manager.OpenFeedbackCommand.ExecuteAsync(null); Assert.AreSame(tab, f.Navigation.SelectedTab);
        using var detail = new PurchaseOrderDetailViewModel(null!, null!, f.Dialogs, f.Messages, f.Navigation, f.User)
        { PurchaseOrderId = f.Context.PurchaseOrderId };
        await detail.OpenFeedbackCommand.ExecuteAsync(null); Assert.AreSame(tab, f.Navigation.SelectedTab);
        Assert.AreEqual("原录入", document.ReportNote); Assert.HasCount(1, f.Navigation.OpenTabs);
        f.Report = _ => Failure(ServiceErrorCodes.OutcomeUnknown); await document.ReportProgressCommand.ExecuteAsync(null);
        await PurchaseOrderProgressNavigation.OpenAsync(f.Navigation, f.Context.PurchaseOrderId, f.Context.Lines[1].PurchaseOrderLineId);
        Assert.AreEqual(f.Context.Lines[0].PurchaseOrderLineId, document.SelectedLine!.Uid); Assert.IsTrue(document.HasPendingReport);
    }

    /// <summary>先从详情进入反馈后，Shell 菜单仍提供选单；同订单只复用已有反馈文档。</summary>
    [TestMethod]
    public async Task Shell_menu_after_document_entry_still_provides_order_picker()
    {
        using var f = new Fixture();
        var menuEntries = new NavigationRouteRegistry().MenuItems.SelectMany(x => x.Children);
        Assert.IsTrue(menuEntries.Any(x => x.RouteKey == PurchaseOrderProgressWorkspaceViewModel.RouteKey));
        Assert.IsFalse(menuEntries.Any(x => x.RouteKey == PurchaseOrderProgressWorkspaceViewModel.DocumentRouteKey));
        await PurchaseOrderProgressNavigation.OpenAsync(f.Navigation, f.Context.PurchaseOrderId);
        var documentTab = f.Navigation.SelectedTab;
        await f.Navigation.OpenAsync(PurchaseOrderProgressWorkspaceViewModel.RouteKey);
        var picker = (PurchaseOrderProgressWorkspaceViewModel)f.Navigation.SelectedTab!.ContentViewModel!;
        Assert.IsFalse(picker.IsOrderDocument); Assert.HasCount(1, picker.Orders);
        await picker.OpenDrawerCommand.ExecuteAsync(null);
        Assert.AreSame(documentTab, f.Navigation.SelectedTab); Assert.HasCount(2, f.Navigation.OpenTabs);
    }

    /// <summary>写入在途拒绝重复命令；切换用户后迟到成功不能回写新会话界面。</summary>
    [TestMethod]
    public async Task Inflight_report_blocks_duplicate_and_does_not_write_back_after_session_change()
    {
        using var f = new Fixture(); await f.OpenAsync(); f.Vm.ReportNote = "提交中";
        var response = new TaskCompletionSource<ServiceResult<PurchaseOrderProgressReportResult>>(); f.ReportAsync = _ => response.Task;
        var task = f.Vm.ReportProgressCommand.ExecuteAsync(null);
        await f.Vm.ReportProgressCommand.ExecuteAsync(null); await f.Vm.RetryReportCommand.ExecuteAsync(null);
        Assert.HasCount(1, f.Writes); Assert.IsTrue(f.Vm.IsBusy);
        f.SetUser("other");
        response.SetResult(f.Success(JsonSerializer.Deserialize<ReportPurchaseOrderProgressCommand>(f.Writes[0])!)); await task;
        Assert.IsNull(f.Vm.CurrentOrder); Assert.AreEqual("提交中", f.Vm.ReportNote); Assert.AreEqual(0, f.Messages.SuccessCount);
    }

    /// <summary>恢复未决的详情在释放后到达，不能再把冻结输入恢复到已释放页面。</summary>
    [TestMethod]
    public async Task Disposed_pending_recovery_does_not_restore_late_input()
    {
        using var f = new Fixture();
        f.Session.Freeze("u", f.Context.PurchaseOrderId, new() { CommandId = Guid.NewGuid(), PurchaseOrderLineId = f.Context.Lines[0].PurchaseOrderLineId, Note = "待恢复" });
        var detail = new TaskCompletionSource<ServiceResult<PurchaseOrderProgressDetailDto>>(); f.DetailAsync = () => detail.Task;
        var loading = f.OpenAsync(); f.Vm.Dispose(); detail.SetResult(ServiceResult<PurchaseOrderProgressDetailDto>.Ok(f.Context)); await loading;
        Assert.AreEqual(string.Empty, f.Vm.ReportNote); Assert.IsFalse(f.Vm.IsDrawerOpen); Assert.HasCount(0, f.Vm.Lines);
        Assert.IsNotNull(f.Session.Find("u", f.Context.PurchaseOrderId));
    }

    private static ServiceResult<PurchaseOrderProgressReportResult> Failure(string code) => ServiceResult<PurchaseOrderProgressReportResult>.Fail(code, "测试失败");

    /// <summary>提供真实契约形状的内存 API 及现有导航，不接触宿主或数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly CurrentUserService User = new();
        public readonly PurchaseOrderProgressCommandSession Session = new("https://feedback-api/");
        public readonly DialogStub Dialogs = new();
        public readonly MessageStub Messages = new();
        public readonly IApiCallRunner Runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        public readonly List<string> Writes = [];
        public readonly List<PurchaseOrderProgressSearchRequest> Searches = [];
        public readonly PurchaseOrderProgressDetailDto Context = new()
        {
            PurchaseOrderId = Guid.NewGuid(), Version = 4, OrderNo = "PO-FEEDBACK", SupplierName = "测试供应商",
            CanReportProgress = true, ConfirmationStatus = ConfirmationStatusEnum.已确认, LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
            Lines = [new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 2, LineNumber = 1, Sku = "A", Quantity = 10 },
                new() { PurchaseOrderLineId = Guid.NewGuid(), Version = 3, LineNumber = 2, Sku = "B", Quantity = 20 }]
        };
        public bool FailDetail;
        public Func<Task<ServiceResult<PurchaseOrderProgressDetailDto>>>? DetailAsync;
        public Func<ReportPurchaseOrderProgressCommand, ServiceResult<PurchaseOrderProgressReportResult>>? Report;
        public Func<ReportPurchaseOrderProgressCommand, Task<ServiceResult<PurchaseOrderProgressReportResult>>>? ReportAsync;
        public readonly IPurchaseOrderProgressApi Api;
        public readonly NavigationService Navigation;
        public readonly PurchaseOrderProgressWorkspaceViewModel Vm;
        public Fixture()
        {
            SetUser("u"); Api = DispatchProxy.Create<IPurchaseOrderProgressApi, ApiProxy>();
            ((ApiProxy)(object)Api).Call = (name, args) => name switch
            {
                nameof(IPurchaseOrderProgressApi.SearchAsync) => Search((PurchaseOrderProgressSearchRequest)args[0]!),
                nameof(IPurchaseOrderProgressApi.GetDetailAsync) => DetailAsync?.Invoke() ?? Task.FromResult(FailDetail
                    ? ServiceResult<PurchaseOrderProgressDetailDto>.Fail(ServiceErrorCodes.ServiceUnavailable, "读取失败")
                    : ServiceResult<PurchaseOrderProgressDetailDto>.Ok(Context)),
                nameof(IPurchaseOrderProgressApi.ReportAsync) => Write((ReportPurchaseOrderProgressCommand)args[1]!),
                _ => throw new NotSupportedException(name)
            };
            var registry = new NavigationRouteRegistry();
            Navigation = new(new Factory(() => CreateVm()), registry, new NavigationAccessService(registry, User), User,
                new StatusMessageService(), NullLogger<NavigationService>.Instance);
            Vm = CreateVm();
        }
        public PurchaseOrderProgressWorkspaceViewModel CreateVm() => new(Api, Runner, Dialogs, Messages, User, Navigation, Session);
        public PurchaseOrderProgressOpenRequest Request(Guid? line = null) => new() { PurchaseOrderId = Context.PurchaseOrderId, PurchaseOrderLineId = line };
        public Task OpenAsync() => Vm.InitializeDocumentAsync(Request(), CancellationToken.None);
        public void SetUser(string id, bool view = true, bool report = true) => User.SetCurrentUser(new()
        {
            UserId = id, Permissions = new[] { PermissionKeys.ProcurementOrderRead }
                .Concat(view ? new[] { PermissionKeys.ProcurementProgressView } : [])
                .Concat(report ? new[] { PermissionKeys.ProcurementProgressReport } : []).ToList()
        });
        private Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> Search(PurchaseOrderProgressSearchRequest request)
        {
            Searches.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>.Ok(new()
            { PageIndex = 1, PageSize = 20, TotalCount = 1, Items = [new() { PurchaseOrderId = Context.PurchaseOrderId, CanReportProgress = true }] }));
        }
        private Task<ServiceResult<PurchaseOrderProgressReportResult>> Write(ReportPurchaseOrderProgressCommand command)
        { Writes.Add(JsonSerializer.Serialize(command)); return ReportAsync?.Invoke(command) ?? Task.FromResult(Report?.Invoke(command) ?? Success(command)); }
        public ServiceResult<PurchaseOrderProgressReportResult> Success(ReportPurchaseOrderProgressCommand command) => ServiceResult<PurchaseOrderProgressReportResult>.Ok(new()
        { PurchaseOrderId = Context.PurchaseOrderId, SupplierProgressReportId = Guid.NewGuid(), Version = command.ExpectedPurchaseOrderVersion });
        public void Dispose() { Vm.Dispose(); Navigation.Dispose(); }
    }

    /// <summary>将 API 调用交给各用例显式安排，未支持的端点不默认放行。</summary>
    public class ApiProxy : DispatchProxy
    {
        /// <summary>按真实方法参数返回内存协议结果。</summary>
        public Func<string, object?[], object?> Call { get; set; } = null!;
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }
    private sealed class Factory(Func<PurchaseOrderProgressWorkspaceViewModel> create) : INavigationTabFactory
    { public INavigationTabLease Create(NavigationRoute route) => new Lease(create()); }
    private sealed class Lease(PurchaseOrderProgressWorkspaceViewModel model) : INavigationTabLease
    { public object ViewModel => model; public void Dispose() => model.Dispose(); }
    private sealed class DialogStub : IDialogService
    {
        public Func<Task<bool>> Confirm = () => Task.FromResult(true);
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Confirm();
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MessageStub : IMessageService
    {
        public int SuccessCount;
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { SuccessCount++; }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
