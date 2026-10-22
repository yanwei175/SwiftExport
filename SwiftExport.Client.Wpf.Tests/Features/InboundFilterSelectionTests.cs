using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.Services;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Resources.Controls.Filter;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证入库多选条件的独立请求快照及导航同步。</summary>
public sealed partial class InboundManagerQueryTests
{
    /// <summary>查询冻结完整多选；刷新和翻页沿用成功条件，空集合明确表示不限。</summary>
    [TestMethod]
    public async Task MultiFilters_FreezeInFlightAndAppliedSelection()
    {
        var api = new ApiStub { Pending = new() }; using var vm = Create(api);
        vm.SelectedDocumentTypes.Add(InboundDocumentTypeEnum.ProductionReceipt);
        vm.SelectedPostingStatuses.Add(InboundPostingStatusEnum.Posted);
        var search = vm.SearchCommand.ExecuteAsync(null); var sent = api.Requests.Single();
        vm.SelectedDocumentTypes.Clear(); vm.SelectedPostingStatuses.Clear();
        CollectionAssert.AreEqual(new[] { InboundDocumentTypeEnum.PurchaseReceipt, InboundDocumentTypeEnum.ProductionReceipt }, sent.DocumentTypes);
        CollectionAssert.AreEqual(new[] { InboundPostingStatusEnum.Unposted, InboundPostingStatusEnum.Posted }, sent.PostingStatuses);
        api.Pending.SetResult(ServiceResult<PagedResponseBase<InboundManagerListItemDto>>.Ok(new() { Items = [], TotalCount = 0 }));
        await search; api.Pending = null;
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 });
        CollectionAssert.AreEqual(sent.DocumentTypes, api.Requests.Last().DocumentTypes);
        CollectionAssert.AreEqual(sent.PostingStatuses, api.Requests.Last().PostingStatuses);
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(0, api.Requests.Last().DocumentTypes.Count); Assert.AreEqual(0, api.Requests.Last().PostingStatuses.Count);
    }

    /// <summary>采购追溯导航按 Uid 读取并用最新详情重设展示条件，不再查询旧状态列表。</summary>
    [TestMethod]
    public async Task MultiFilters_NavigationResetsSelectionToRequestedContext()
    {
        var api = new ApiStub(); using var vm = Create(api); await vm.SearchCommand.ExecuteAsync(null);
        var uid = vm.SelectedItem!.Uid;
        vm.SelectedDocumentTypes.Add(InboundDocumentTypeEnum.ProductionReceipt); vm.SelectedPostingStatuses.Add(InboundPostingStatusEnum.Posted);
        api.DetailHandler = id => Task.FromResult(DetailFor(id, "最新详情"));
        await vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = uid, DocumentNo = "IR-1", PostingStatus = InboundPostingStatusEnum.Reversed }, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { InboundDocumentTypeEnum.PurchaseReceipt }, vm.SelectedDocumentTypes.ToArray());
        CollectionAssert.AreEqual(new[] { InboundPostingStatusEnum.Unposted }, vm.SelectedPostingStatuses.ToArray());
        Assert.AreEqual(1, api.Requests.Count); Assert.AreEqual(2, api.DetailCalls);
    }

    /// <summary>真实界面使用多选控件并绑定可提交的集合，候选中文模板沿用现有枚举显示。</summary>
    [TestMethod]
    public void View_FiltersBindMultiSelectCollections()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var vm = Create(new ApiStub());
            var view = new InboundManagerView { DataContext = vm };
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var types = (MultiSelectLookupButton)view.FindName("DocumentTypesFilter");
            var statuses = (MultiSelectLookupButton)view.FindName("PostingStatusesFilter");
            Assert.AreSame(vm.SelectedDocumentTypes, types.SelectedItems); Assert.AreSame(vm.SelectedPostingStatuses, statuses.SelectedItems);
            Assert.AreEqual(2, types.ItemsSource!.Cast<object>().Count()); Assert.AreEqual(3, statuses.ItemsSource!.Cast<object>().Count());
            types.SelectedItems!.Clear(); types.SelectedItems.Add(InboundDocumentTypeEnum.ProductionReceipt);
            statuses.SelectedItems!.Add(InboundPostingStatusEnum.Reversed);
            await vm.SearchCommand.ExecuteAsync(null);
            Assert.IsTrue(vm.SelectedDocumentTypes.Contains(InboundDocumentTypeEnum.ProductionReceipt));
            Assert.IsTrue(vm.SelectedPostingStatuses.Contains(InboundPostingStatusEnum.Reversed));
        });
    }

    /// <summary>删除后选到尚未加载的前一条摘要时，写入 Busy 结束后补载详情。</summary>
    [TestMethod]
    public async Task Delete_SelectsPreviousAndLoadsItsDetailAfterWrite()
    {
        var api = new ApiStub();
        using var vm = new InboundManagerViewModel(new WarehouseStub(), new SourceStub(), new DialogStub(), api, new RunnerStub(),
            new InboundPersistenceService(api, Microsoft.Extensions.Logging.Abstractions.NullLogger<InboundPersistenceService>.Instance),
            new AlwaysConfirmDelete(), postingUser: InboundTestUsers.Create());
        await vm.SearchCommand.ExecuteAsync(null);
        var first = vm.SelectedItem!; var last = vm.Headers.Items[1];
        vm.SelectedItem = last; await vm.LoadSelectedDetailCommand.ExecuteAsync(null); first.IsDetailLoaded = false;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(first, vm.SelectedItem); Assert.IsTrue(first.IsDetailLoaded); Assert.AreEqual(3, api.DetailCalls);
        Assert.IsFalse(vm.IsBusy); Assert.IsFalse(vm.HeaderReadOnly);
    }

    /// <summary>为持久化删除提供明确原因，不创建真实弹窗。</summary>
    private sealed class AlwaysConfirmDelete : IInboundDeleteDialogService
    {
        /// <inheritdoc/>
        public Task<string?> ConfirmAsync(int count, CancellationToken token) => Task.FromResult<string?>("测试删除");
    }
}

/// <summary>验证头、明细删除后的实际选择及保存后的草稿准入。</summary>
public sealed partial class InboundManagerPersistenceTests
{
    /// <summary>真实 View 删除中间、末尾、首行、唯一行后选择前一幸存行或安全清空。</summary>
    [TestMethod]
    [DataRow(3, 1, 0)]
    [DataRow(3, 2, 1)]
    [DataRow(3, 0, 1)]
    [DataRow(1, 0, -1)]
    public void View_DeleteHeaderKeepsPreviousSelection(int count, int deleting, int expected)
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var rows = Enumerable.Range(0, count).Select(_ => f.Add()).ToArray();
            var view = new InboundManagerView { DataContext = f.Vm };
            var window = new Window { Content = view, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); f.Vm.SelectedItem = rows[deleting]; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var target = expected < 0 ? null : rows[expected]; Assert.AreSame(target, f.Vm.SelectedItem);
                Assert.AreSame(target, ((DataGrid)view.FindName("HeaderGrid")).SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>表格排序后按显示顺序定位上一行，不按原集合的插入顺序。</summary>
    [TestMethod]
    public void View_DeleteHeaderUsesDisplayedSortOrder()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var first = f.Add(); var second = f.Add(); var third = f.Add();
            first.ReceiptDate = DateTime.Today; second.ReceiptDate = DateTime.Today.AddDays(1); third.ReceiptDate = DateTime.Today.AddDays(2);
            var view = new InboundManagerView { DataContext = f.Vm };
            var window = new Window { Content = view, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); var grid = (DataGrid)view.FindName("HeaderGrid");
                grid.Items.SortDescriptions.Add(new(nameof(InboundHeaderModel.ReceiptDate), ListSortDirection.Descending));
                f.Vm.SelectedItem = second; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreSame(third, grid.Items[0]); await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.AreSame(third, f.Vm.SelectedItem); Assert.AreSame(third, grid.SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>明细删除覆盖前一行、首行后继和全空；基类删除池仍记录持久化行。</summary>
    [TestMethod]
    [DataRow(2)]
    [DataRow(0)]
    public async Task DeleteLines_SelectsNeighborAndHandlesEmpty(int deleting)
    {
        using var f = new Fixture(); var row = f.Add(); f.AddLine(row); f.AddLine(row); await f.Vm.SaveCommand.ExecuteAsync(null);
        var lines = row.Lines.Items.ToArray(); row.Lines.SelectedItem = lines[deleting];
        f.Vm.DeleteSelectedLineCommand.Execute(null);
        Assert.AreSame(lines[1], row.Lines.SelectedItem);
        Assert.AreEqual(1, row.Lines.DeletedItems.Count);
        foreach (var line in row.Lines.Items) line.IsChecked = true;
        f.Vm.DeleteSelectedLineCommand.Execute(null); Assert.AreEqual(0, row.Lines.Items.Count); Assert.IsNull(row.Lines.SelectedItem);
    }

    /// <summary>明细真实 CurrentItem 绑定在首行、末行及唯一行删除后保持邻行或安全清空。</summary>
    [TestMethod]
    [DataRow(3, 0, 1)]
    [DataRow(3, 2, 1)]
    [DataRow(1, 0, -1)]
    public void View_DeleteLineKeepsNativeCurrentItem(int count, int deleting, int expected)
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); using var f = new Fixture(); var header = f.Add();
            for (var i = 1; i < count; i++) f.AddLine(header);
            var rows = header.Lines.Items.ToArray(); var view = new InboundManagerView { DataContext = f.Vm };
            var window = new Window { Content = view, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); header.Lines.SelectedItem = rows[deleting]; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                f.Vm.DeleteSelectedLineCommand.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var target = expected < 0 ? null : rows[expected]; Assert.AreSame(target, header.Lines.SelectedItem);
                Assert.AreSame(target, ((DataGrid)view.FindName("LineGrid")).CurrentItem);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>批量成功按真实逐次删除定位最后删除行的前一幸存行，全删后选择为空。</summary>
    [TestMethod]
    public async Task DeleteBatch_UsesLastSuccessfulNeighborAndClearsWhenEmpty()
    {
        using var f = new Fixture(); var first = f.Add(); var second = f.Add(); var third = f.Add();
        second.IsChecked = true; third.IsChecked = true; await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(first, f.Vm.SelectedItem); Assert.AreEqual(1, f.Vm.Headers.Items.Count);
        first.IsChecked = true; await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Vm.Headers.Items.Count); Assert.IsNull(f.Vm.SelectedItem);
    }

    /// <summary>批量部分失败只移除真实成功行，失败行保留选择便于查看反馈和恢复。</summary>
    [TestMethod]
    public async Task DeleteBatch_PartialFailureRetainsFailedRowAndSurvivors()
    {
        using var f = new Fixture(); var first = f.Add(); var second = f.Add(); var third = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        second.IsChecked = true; third.IsChecked = true; f.Service.FailDeleteAt = 2;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(third, f.Vm.SelectedItem); Assert.IsFalse(f.Vm.Headers.Items.Contains(second));
        Assert.IsTrue(f.Vm.Headers.Items.Contains(first)); Assert.IsTrue(f.Vm.Headers.Items.Contains(third));
        Assert.AreEqual(2, f.Service.Deletes.Count);
    }

    /// <summary>未知删除保留当前行，原命令恢复成功才定位上一行，不重复产生删除事实。</summary>
    [TestMethod]
    public async Task DeleteRecovery_SelectsPreviousOnlyAfterConfirmedSuccess()
    {
        using var f = new Fixture(); var first = f.Add(); var second = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        f.Vm.SelectedItem = second; f.Service.LoseNextResponse = true;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null); Assert.AreSame(second, f.Vm.SelectedItem);
        var command = f.Service.Deletes.Single(); await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(first, f.Vm.SelectedItem); Assert.AreSame(command, f.Service.Deletes.Last()); Assert.AreEqual(3, f.Service.Writes);
    }

    /// <summary>保存不是草稿只读边界；获取依赖编辑权限和人工未过账资格，已保存单据仍限制修改类型。</summary>
    [TestMethod]
    public async Task SavedDraft_CanFetchSourcesUntilPostingReadOnlyOrPermissionLoss()
    {
        using var f = new Fixture(); var row = f.Add(); await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.OpenInboundSourceDialogCommand.CanExecute(null)); Assert.IsFalse(f.Vm.LinesReadOnly);
        Assert.IsFalse(row.CanChangeDocumentType);
        f.User.SetCurrentUser(new() { UserId = "inbound-test", Permissions = [PermissionKeys.InventoryInboundView] });
        Assert.IsFalse(f.Vm.OpenInboundSourceDialogCommand.CanExecute(null));
        f.User.SetCurrentUser(InboundTestUsers.Create().CurrentUser!); await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.OpenInboundSourceDialogCommand.CanExecute(null));
        using var readOnly = new Fixture(); var draft = readOnly.Add(); await readOnly.Vm.SaveCommand.ExecuteAsync(null);
        await readOnly.Vm.InitializeDocumentAsync(new InboundDocumentOpenRequest { InboundDocumentId = draft.Uid }, CancellationToken.None);
        Assert.IsFalse(readOnly.Vm.OpenInboundSourceDialogCommand.CanExecute(null));
    }
}
