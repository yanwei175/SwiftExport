using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购管理批量中断统计、筛选边界及真实分页控件的定向回归。</summary>
public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>前序成功、失败和预检跳过后撤权，当前及后续计划都必须落入未执行统计。</summary>
    [TestMethod]
    public async Task ManagerFollowup_RevocationAccountsForEveryOrder()
    {
        var rows = new[] { Order("OK"), Order("FAIL"), Order("SKIP"), Order("REVOKE"), Order("LATER1"), Order("LATER2") };
        rows[2].CanConfirm = false; using var f = new FollowupFixture(rows); await f.Vm.InitializeAsync(); f.Vm.IsAllCurrentPageChecked = true;
        f.Writes.ConfirmHandler = (id, command) =>
        {
            if (id == rows[3].PurchaseOrderId) f.User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementOrderRead] });
            return Task.FromResult(id == rows[1].PurchaseOrderId
                ? ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.VersionConflict, "版本冲突")
                : ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(id, command.ExpectedVersion)));
        };
        await f.Vm.BatchConfirmCommand.ExecuteAsync(null);
        Assert.HasCount(3, f.Writes.ConfirmCalls);
        StringAssert.Contains(f.Vm.ResultSummary, "成功 2，跳过 1，失败 1，未知 0，未执行 2");
        Assert.AreEqual("批量确认已停止", f.Dialogs.ContentTitle);
        StringAssert.Contains(f.Dialogs.Content, "LATER1"); StringAssert.Contains(f.Dialogs.Content, "LATER2");
    }

    /// <summary>预检或汇总确认期间撤权，所有可执行计划必须说明未执行，不能弹成功完成结果。</summary>
    [TestMethod]
    [DataRow(true)][DataRow(false)]
    public async Task ManagerFollowup_RevocationBeforeWritesStopsWholePlan(bool duringPreflight)
    {
        var rows = new[] { Order("FIRST"), Order("SECOND") }; using var f = new FollowupFixture(rows);
        await f.Vm.InitializeAsync(); f.Vm.IsAllCurrentPageChecked = true;
        void Revoke() => f.User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementOrderRead] });
        if (duringPreflight) f.Writes.ContextHandler = id => { Revoke(); return Task.FromResult(ServiceResult<ProcurementConfirmationContextDto>.Ok(Context(rows.Single(x => x.PurchaseOrderId == id)))); };
        else f.Dialogs.ConfirmAction = Revoke;
        await f.Vm.BatchConfirmCommand.ExecuteAsync(null);
        Assert.HasCount(0, f.Writes.ConfirmCalls); StringAssert.Contains(f.Vm.ResultSummary, "未执行 2");
        Assert.AreEqual("批量确认已停止", f.Dialogs.ContentTitle);
    }

    /// <summary>写响应回来时账号已切换或页面已释放，不能向新会话发布旧结果或触发最终刷新。</summary>
    [TestMethod]
    [DataRow(true)][DataRow(false)]
    public async Task ManagerFollowup_OldBatchCannotPublishAfterSwitchOrDispose(bool switchAccount)
    {
        var rows = new[] { Order("OLD1"), Order("OLD2") }; using var f = new FollowupFixture(rows);
        await f.Vm.InitializeAsync(); f.Vm.IsAllCurrentPageChecked = true;
        var response = new TaskCompletionSource<ServiceResult<ProcurementOrderCommandResult>>(); f.Writes.ConfirmHandler = (_, _) => response.Task;
        var running = f.Vm.BatchConfirmCommand.ExecuteAsync(null);
        if (switchAccount) f.User.SetCurrentUser(new() { UserId = "other", Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderConfirm] });
        else f.Vm.Dispose();
        f.Vm.ResultSummary = "当前会话";
        response.SetResult(ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(rows[0].PurchaseOrderId, 1))); await running;
        Assert.HasCount(1, f.Writes.ConfirmCalls); Assert.AreEqual("当前会话", f.Vm.ResultSummary); Assert.IsNull(f.Dialogs.ContentTitle);
        Assert.AreEqual(1, f.Queries.SearchCount);
    }

    /// <summary>查询按钮和实际请求使用同一去重后的文本及多选上限，超限保留输入而不发送。</summary>
    [TestMethod]
    [DataRow("keyword", 100, true)][DataRow("keyword", 101, false)]
    [DataRow("customer", 500, true)][DataRow("customer", 501, false)]
    [DataRow("supplier", 500, true)][DataRow("supplier", 501, false)]
    [DataRow("duplicateCustomer", 501, true)][DataRow("duplicateSupplier", 501, true)]
    public async Task ManagerFollowup_QueryLimitsMatchCommandAndRequest(string kind, int count, bool valid)
    {
        using var f = new FollowupFixture([Order("PO")]); var vm = f.Vm;
        if (kind == "keyword") vm.Keyword = " " + new string('查', count) + " ";
        else for (var index = 0; index < count; index++)
        {
            if (kind.Contains("Customer", StringComparison.OrdinalIgnoreCase)) vm.SelectedCustomers.Add(new() { CustomerCode = kind == "duplicateCustomer" ? "C" : $"C{index}" });
            else vm.SelectedSuppliers.Add(new() { Uid = kind == "duplicateSupplier" ? f.DuplicateSupplier : Guid.NewGuid() });
        }
        Assert.AreEqual(valid, vm.SearchCommand.CanExecute(null)); Assert.AreEqual(valid, vm.ApplyMoreFiltersCommand.CanExecute(null));
        await vm.InitializeAsync(); Assert.AreEqual(valid ? 1 : 0, f.Queries.SearchCount);
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(valid ? 2 : 0, f.Queries.SearchCount);
        if (!valid) { StringAssert.Contains(vm.ResultSummary, count == 101 ? "100" : "500"); Assert.IsTrue(vm.ClearFiltersCommand.CanExecute(null)); }
        if (kind == "keyword") Assert.AreEqual(count + 2, vm.Keyword.Length);
    }

    /// <summary>纠正到边界值后撤销本地提示并恢复查询资格，期间产生的命令结果仍原样保留。</summary>
    [TestMethod]
    [DataRow("keyword")][DataRow("customer")][DataRow("supplier")]
    public async Task ManagerFollowup_CorrectionClearsOnlyInputMessage(string kind)
    {
        using var f = new FollowupFixture([Order("PO")]); await f.Vm.InitializeAsync(); var original = f.Vm.ResultSummary;
        void SetInvalid()
        {
            if (kind == "keyword") f.Vm.Keyword = new string('查', 101);
            else if (kind == "customer") { while (f.Vm.SelectedCustomers.Count < 501) f.Vm.SelectedCustomers.Add(new() { CustomerCode = $"C{f.Vm.SelectedCustomers.Count}" }); }
            else { while (f.Vm.SelectedSuppliers.Count < 501) f.Vm.SelectedSuppliers.Add(new() { Uid = Guid.NewGuid() }); }
        }
        void Correct()
        {
            if (kind == "keyword") f.Vm.Keyword = new string('查', 100);
            else if (kind == "customer") f.Vm.SelectedCustomers.RemoveAt(500);
            else f.Vm.SelectedSuppliers.RemoveAt(500);
        }
        SetInvalid(); Assert.IsFalse(f.Vm.SearchCommand.CanExecute(null)); Assert.AreNotEqual(original, f.Vm.ResultSummary);
        Correct(); Assert.IsTrue(f.Vm.SearchCommand.CanExecute(null)); Assert.AreEqual(original, f.Vm.ResultSummary);
        SetInvalid(); f.Vm.ResultSummary = "保留新的命令结果"; Correct(); Assert.AreEqual("保留新的命令结果", f.Vm.ResultSummary);
    }

    /// <summary>未应用的超限输入不妨碍按已应用条件刷新和翻页，清空仍可恢复合法新查询。</summary>
    [TestMethod]
    public async Task ManagerFollowup_InvalidInputKeepsAppliedQueryAvailable()
    {
        using var f = new FollowupFixture([Order("PO")]); PurchaseOrderManagerSearchRequest? last = null;
        f.Queries.SearchHandler = request => { last = request; return Task.FromResult(FollowupPage(request.PageIndex, request.PageSize)); };
        f.Vm.Keyword = "已应用"; await f.Vm.InitializeAsync(); f.Vm.Keyword = new string('查', 101);
        Assert.IsFalse(f.Vm.SearchCommand.CanExecute(null)); Assert.IsTrue(f.Vm.RefreshCommand.CanExecute(null));
        await f.Vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("已应用", last!.Keyword);
        Assert.IsTrue(f.Vm.PageChangeCommand.CanExecute(null));
        await f.Vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 20 }); Assert.AreEqual("已应用", last.Keyword);
        await f.Vm.ClearFiltersCommand.ExecuteAsync(null); Assert.AreEqual(string.Empty, last.Keyword); Assert.IsTrue(f.Vm.SearchCommand.CanExecute(null));
    }

    /// <summary>实际分页控件请求期间保持已接纳事实，失败恢复旧页，成功接纳新页；Busy不发送第二次请求。</summary>
    [TestMethod]
    [DataRow(true)][DataRow(false)]
    public void ManagerFollowup_PageNavigatorKeepsAcceptedFacts(bool success) => Controls.DatePickerAssistTests.RunWithApplication(async () =>
    {
        TestInfrastructure.ApplicationResourceLoader.Load(); using var f = new FollowupFixture([Order("PO")]);
        f.Queries.SearchHandler = request => Task.FromResult(FollowupPage(request.PageIndex, request.PageSize));
        f.Vm.PageIndex = 2; await f.Vm.InitializeAsync();
        var navigator = new PageNavigator { DataContext = f.Vm };
        foreach (var pair in new[] { (PageNavigator.PageIndexProperty, "PageIndex"), (PageNavigator.PageSizeProperty, "PageSize"), (PageNavigator.TotalCountProperty, "TotalCount"), (PageNavigator.PageChangeCommandProperty, "PageChangeCommand") })
            navigator.SetBinding(pair.Item1, new Binding(pair.Item2));
        var window = PriceWindow(navigator);
        try
        {
            window.Show(); await PriceIdle(); var combo = (ComboBox)navigator.FindName("CboPageSize");
            var response = new TaskCompletionSource<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>();
            f.Queries.SearchHandler = _ => response.Task; combo.SelectedItem = 50;
            Assert.IsTrue(f.Vm.IsBusy); Assert.AreEqual(20, combo.SelectedItem); Assert.AreEqual(20, f.Vm.PageSize); Assert.AreEqual(2, f.Vm.PageIndex);
            combo.SelectedItem = 100; Assert.AreEqual(20, combo.SelectedItem); Assert.AreEqual(2, f.Queries.SearchCount);
            response.SetResult(success ? FollowupPage(1, 50) : ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("查询失败"));
            await f.Vm.PageChangeCommand.ExecutionTask!; await PriceIdle();
            Assert.AreEqual(success ? 50 : 20, combo.SelectedItem); var oldIndex = success ? 1 : 2;
            Assert.AreEqual(oldIndex, navigator.PageItems.Single(x => x.IsCurrent).PageIndex);
            var pageResponse = new TaskCompletionSource<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>(); f.Queries.SearchHandler = _ => pageResponse.Task;
            FollowupChildren<Button>(navigator).Single(x => x.DataContext is PageItem item && item.PageIndex == 3).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(oldIndex, navigator.PageIndex);
            pageResponse.SetResult(success ? FollowupPage(3, 50) : ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("查询失败"));
            await f.Vm.PageChangeCommand.ExecutionTask!; await PriceIdle();
            Assert.AreEqual(success ? 3 : oldIndex, navigator.PageItems.Single(x => x.IsCurrent).PageIndex);
            navigator.PageChangeCommand = new RelayCommand<PageChangeRequest>(request => { navigator.PageIndex = request!.NewIndex; navigator.PageSize = request.NewSize; });
            combo.SelectedItem = 100; Assert.AreEqual(100, navigator.PageSize); Assert.AreEqual(100, combo.SelectedItem);
        }
        finally { window.Close(); await PriceIdle(); }
    });

    /// <summary>只在测试中查找真实生成的分页按钮。</summary>
    private static IEnumerable<T> FollowupChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); if (child is T match) yield return match;
            foreach (var item in FollowupChildren<T>(child)) yield return item;
        }
    }

    /// <summary>提供足够页数，分别验证当前页高亮和页大小接纳。</summary>
    private static ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>> FollowupPage(int page, int size) =>
        ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Ok(new() { PageIndex = page, PageSize = size, TotalCount = 200 });

    /// <summary>复用真实用户通知和内存API，记录旧会话是否仍发布弹窗。</summary>
    private sealed class FollowupFixture : IDisposable
    {
        public CurrentUserService User { get; } = new();
        public ManagerApiStub Queries { get; }
        public OrderApiStub Writes { get; }
        public FollowupDialogs Dialogs { get; } = new();
        public PurchaseOrderManagerViewModel Vm { get; }
        public Guid DuplicateSupplier { get; } = Guid.NewGuid();
        /// <summary>权限和所有响应均由本地夹具提供。</summary>
        public FollowupFixture(PurchaseOrderManagerListItemDto[] rows)
        {
            User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementOrderConfirm] });
            Queries = new(rows); Writes = new(rows);
            Vm = new(Queries, Writes, new ProcurementDialogStub(), new NavigationStub(), Dialogs, new ApiCallRunnerStub(), new MessageStub(), User);
        }
        /// <summary>解除订阅，避免测试间保留管理页实例。</summary>
        public void Dispose() => Vm.Dispose();
    }

    /// <summary>记录确认与结果展示，不创建真实弹窗。</summary>
    private sealed class FollowupDialogs : IDialogService
    {
        public Action? ConfirmAction { get; set; }
        public string? ContentTitle { get; private set; }
        public string Content { get; private set; } = string.Empty;
        /// <inheritdoc/>
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        { ConfirmAction?.Invoke(); return Task.FromResult(true); }
        /// <inheritdoc/>
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default)
        { ContentTitle = title; Content = content.ToString()!; return Task.CompletedTask; }
        /// <inheritdoc/>
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
