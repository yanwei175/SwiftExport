using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Inventory.WarehouseSites.Models;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Views;
using SwiftExportClient.Features.Suppliers.Components;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory.WarehouseSites;

/// <summary>验证供应商虚拟仓页面的权限、编辑隔离、确认边界、稳定重试和请求竞态。</summary>
[TestClass]
public sealed class WarehouseSitesViewModelTests
{
    /// <summary>验证导航稳定键存在，且只有模块访问权限的账号不能执行管理命令。</summary>
    [TestMethod]
    public void RouteAndPermission_ShouldKeepAccessOnlyUserReadOnly()
    {
        using var viewModel = CreateViewModel(new FakeWarehouseApi(), permissions: ["Inventory.Warehouse.View"]);

        Assert.AreEqual("inventory-warehouse-sites", WarehouseSitesViewModel.RouteKey);
        Assert.IsTrue(viewModel.IsReadOnly);
        Assert.IsFalse(viewModel.HasManagePermission);
        Assert.IsFalse(viewModel.OpenCreateCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ActivateCommand.CanExecute(null));
    }

    /// <summary>验证两处供应商控件复用同一缓存，抽屉仍通过页面代理解析页面级命令。</summary>
    [TestMethod]
    public void DrawerBindings_ShouldResolvePageViewModelThroughProxy()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var supplier = Supplier("S-01", "供应商甲");
            using var viewModel = CreateViewModel(
                new FakeWarehouseApi(),
                cache: new ReferenceDataCacheServiceStub(suppliers: [supplier]));
            await viewModel.OpenCreateCommand.ExecuteAsync(null);
            var view = new WarehouseSitesView { DataContext = viewModel };
            var window = new Window
            {
                Width = 900,
                Height = 640,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
                Content = view
            };
            using var output = new StringWriter();
            using var listener = new TextWriterTraceListener(output);
            var trace = PresentationTraceSources.DataBindingSource;
            var previousLevel = trace.Switch.Level;
            trace.Switch.Level = SourceLevels.Error;
            trace.Listeners.Add(listener);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                view.UpdateLayout();

                var supplierEditors = FindVisualDescendants<SupplierAutoSuggestComboBox>(view).ToList();
                Assert.AreEqual(2, supplierEditors.Count);
                var editor = supplierEditors
                    .Single(candidate => BindingOperations
                        .GetBinding(candidate, SupplierAutoSuggestComboBox.SelectedSupplierProperty)
                        ?.Path.Path == "SelectedSupplier");
                Assert.AreSame(viewModel.SupplierOptions, editor.ItemsSource);
                Assert.IsTrue(supplierEditors.All(candidate => ReferenceEquals(viewModel.SupplierOptions, candidate.ItemsSource)));

                var buttons = FindVisualDescendants<Button>(view).ToList();
                Assert.IsTrue(buttons.Any(button => Equals(button.Content, "保存") && ReferenceEquals(button.Command, viewModel.SaveCommand)));
                Assert.AreEqual(2, buttons.Count(button => Equals(button.Content, "取消") && ReferenceEquals(button.Command, viewModel.CloseDrawerCommand)));

                listener.Flush();
                var bindingErrors = output.ToString();
                Assert.IsFalse(bindingErrors.Contains("SupplierOptions", StringComparison.Ordinal), bindingErrors);
                Assert.IsFalse(bindingErrors.Contains("SaveCommand", StringComparison.Ordinal), bindingErrors);
                Assert.IsFalse(bindingErrors.Contains("CloseDrawerCommand", StringComparison.Ordinal), bindingErrors);
            }
            finally
            {
                window.Close();
                trace.Listeners.Remove(listener);
                trace.Switch.Level = previousLevel;
            }
        });
    }

    /// <summary>验证页面初始化只加载共享缓存，不再调用仓库专用供应商远程搜索。</summary>
    [TestMethod]
    public async Task Initialize_ShouldLoadSharedSupplierCacheWithoutWarehouseSupplierSearch()
    {
        var api = new FakeWarehouseApi();
        var cache = new ReferenceDataCacheServiceStub(suppliers: [Supplier("S-01", "供应商甲")]);
        using var viewModel = CreateViewModel(api, cache: cache);

        await viewModel.InitializeAsync();

        Assert.AreEqual(1, cache.EnsureLoadedCallCount);
        Assert.AreEqual(1, viewModel.SupplierOptions.Count());
        Assert.AreEqual(0, api.SupplierSearchCallCount);
    }

    /// <summary>验证缓存加载失败会如实提示，但不阻断虚拟仓列表查询。</summary>
    [TestMethod]
    public async Task Initialize_WhenCacheFails_ShouldReportAndStillLoadWarehouseList()
    {
        var api = new FakeWarehouseApi();
        var messages = new FakeMessageService();
        var cache = new ReferenceDataCacheServiceStub(
            ensureSuccess: false,
            ensureMessage: "供应商缓存不可用");
        using var viewModel = CreateViewModel(api, messages: messages, cache: cache);

        await viewModel.InitializeAsync();

        Assert.AreEqual(1, api.SearchCallCount);
        Assert.AreEqual("供应商缓存未更新", messages.LastErrorTitle);
        Assert.AreEqual("供应商缓存不可用", messages.LastErrorMessage);
    }

    /// <summary>验证筛选控件按缓存本地过滤，并把完整选择及显式清空双向写回页面。</summary>
    [TestMethod]
    public void FilterSupplier_ShouldSearchSelectAndClearAgainstLocalCache()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var first = Supplier("S-001", "供应商甲");
            var second = Supplier("S-002", "供应商乙");
            var api = new FakeWarehouseApi();
            using var viewModel = CreateViewModel(
                api,
                cache: new ReferenceDataCacheServiceStub(suppliers: [first, second]));
            var view = new WarehouseSitesView { DataContext = viewModel };
            var window = CreateHiddenWindow(view);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var filter = FindVisualDescendants<SupplierAutoSuggestComboBox>(view)
                    .Single(candidate => BindingOperations
                        .GetBinding(candidate, SupplierAutoSuggestComboBox.SelectedSupplierProperty)
                        ?.Path.Path == nameof(WarehouseSitesViewModel.SearchSupplier));
                var lookup = FindVisualDescendants<LookupTextBox>(filter).Single();
                var input = (TextBox)lookup.FindName("InputTextBox")!;
                var results = (ListBox)lookup.FindName("ResultList")!;

                input.Focus();
                input.Text = second.SupplierCode;
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.AreEqual(1, results.Items.Count);
                Assert.AreSame(second, results.Items[0]);

                filter.SelectedSupplier = second;
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert.AreSame(second, viewModel.SearchSupplier);
                ((Button)lookup.FindName("BtnClear")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert.IsNull(viewModel.SearchSupplier);
                Assert.AreEqual(0, api.SupplierSearchCallCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>验证仓库筛选只提交已确认缓存对象的 Uid，而不提交输入显示文本。</summary>
    [TestMethod]
    public async Task Search_ShouldSubmitSelectedSupplierUid()
    {
        var api = new FakeWarehouseApi();
        var supplier = Supplier("S-01", "供应商甲");
        using var viewModel = CreateViewModel(
            api,
            cache: new ReferenceDataCacheServiceStub(suppliers: [supplier]));
        viewModel.SearchSupplier = supplier;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.SearchRequests.Count);
        Assert.AreEqual(supplier.Uid, api.SearchRequests[0].SupplierId);
        Assert.AreEqual(0, api.SupplierSearchCallCount);
    }

    /// <summary>验证历史供应商不在缓存时仍由详情快照回显，且编辑模式禁止改变归属。</summary>
    [TestMethod]
    public void EditSupplier_WhenMissingFromCache_ShouldKeepSnapshotReadOnly()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var warehouse = Warehouse("WH-01", "历史仓");
            var api = new FakeWarehouseApi { DetailResult = ServiceResult<WarehouseSiteDto>.Ok(warehouse) };
            using var viewModel = CreateViewModel(
                api,
                cache: new ReferenceDataCacheServiceStub(suppliers: [Supplier("S-NEW", "新供应商")]));
            viewModel.SelectedItem = warehouse;
            await viewModel.OpenEditCommand.ExecuteAsync(null);
            var view = new WarehouseSitesView { DataContext = viewModel };
            var window = CreateHiddenWindow(view);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                var editor = FindVisualDescendants<SupplierAutoSuggestComboBox>(view)
                    .Single(candidate => BindingOperations
                        .GetBinding(candidate, SupplierAutoSuggestComboBox.SelectedSupplierProperty)
                        ?.Path.Path == "SelectedSupplier");

                Assert.IsFalse(editor.IsEnabled);
                Assert.AreEqual(warehouse.SupplierId, editor.SelectedSupplier?.Uid);
                Assert.AreEqual(warehouse.SupplierCode, editor.SelectedSupplier?.SupplierCode);
                Assert.IsNotNull(editor.ItemsSource);
                Assert.IsFalse(editor.ItemsSource!.Cast<SupplierDto>().Any(item => item.Uid == warehouse.SupplierId));
                var display = (TextBlock)FindVisualDescendants<LookupTextBox>(editor).Single().FindName("DisplayTextBlock")!;
                // 共享供应商控件显示名称；代码和归属仍由上方快照断言保护。
                Assert.AreEqual(warehouse.SupplierName, editor.SelectedSupplier?.SupplierName);
                Assert.AreEqual(warehouse.SupplierName, display.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>验证关闭新增抽屉会通知新增按钮恢复可用，无需刷新列表。</summary>
    [TestMethod]
    public async Task CloseCreateDrawer_ShouldNotifyAndRestoreCreate()
    {
        using var viewModel = CreateViewModel(new FakeWarehouseApi());
        await viewModel.OpenCreateCommand.ExecuteAsync(null);
        Assert.IsFalse(viewModel.OpenCreateCommand.CanExecute(null));
        var notifications = 0;
        viewModel.OpenCreateCommand.CanExecuteChanged += (_, _) => notifications++;
        await viewModel.CloseDrawerCommand.ExecuteAsync(null);
        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsTrue(viewModel.OpenCreateCommand.CanExecute(null));
        Assert.IsTrue(notifications > 0);
    }

    /// <summary>验证编辑使用独立模型，放弃抽屉不会污染列表 DTO。</summary>
    [TestMethod]
    public async Task EditCancel_ShouldNotMutateListItem()
    {
        var warehouse = Warehouse("WH-01", "原名称");
        var api = new FakeWarehouseApi { DetailResult = ServiceResult<WarehouseSiteDto>.Ok(warehouse) };
        var dialogs = new FakeDialogService { ConfirmResult = true };
        using var viewModel = CreateViewModel(api, dialogs: dialogs);
        viewModel.SelectedItem = warehouse;

        await viewModel.OpenEditCommand.ExecuteAsync(null);
        viewModel.EditModel!.Name = "未保存名称";
        await viewModel.CloseDrawerCommand.ExecuteAsync(null);

        Assert.AreEqual("原名称", warehouse.Name);
        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.AreEqual(1, dialogs.ConfirmCount);
    }

    /// <summary>验证取消删除原因弹窗不会调用服务端删除接口。</summary>
    [TestMethod]
    public async Task DeleteCancel_ShouldNotCallApi()
    {
        var reasonDialog = new FakeReasonDialog { Result = null };
        var api = new FakeWarehouseApi();
        using var viewModel = CreateViewModel(api, reasonDialog: reasonDialog);
        viewModel.SelectedItem = Warehouse("WH-01", "测试仓");

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.AreEqual(0, api.DeleteCallCount);
    }

    /// <summary>取消启用原因确认不得调用 API，也不得刷新仓库缓存。</summary>
    [TestMethod]
    public async Task ActivateCancel_ShouldNotCallApi()
    {
        var reasonDialog = new FakeReasonDialog { Result = null };
        var cache = new ReferenceDataCacheServiceStub();
        var api = new FakeWarehouseApi();
        using var viewModel = CreateViewModel(api, reasonDialog: reasonDialog, cache: cache);
        viewModel.SelectedItem = Warehouse("WH-01", "停用仓", isActive: false);

        await viewModel.ActivateCommand.ExecuteAsync(null);

        Assert.AreEqual("启用", reasonDialog.LastAction);
        Assert.AreEqual(0, api.ActivateCallCount);
        Assert.AreEqual(0, cache.RefreshWarehouseSitesCallCount);
    }

    /// <summary>启用成功后列表与 Picker 缓存分别刷新，缓存失败只追加警告而不覆盖业务成功。</summary>
    [TestMethod]
    public async Task Activate_WhenPickerCacheRefreshFails_ShouldKeepSuccessAndWarnSeparately()
    {
        var target = Warehouse("WH-01", "停用仓", isActive: false);
        var activated = Warehouse(target.Code, target.Name);
        activated.Uid = target.Uid;
        activated.Version = target.Version + 1;
        var api = new FakeWarehouseApi
        {
            ActivateHandler = (_, _) => Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(activated)),
            SearchHandler = (_, _) => Task.FromResult(Page(activated))
        };
        var cache = new ReferenceDataCacheServiceStub(
            warehouseRefreshSuccess: false,
            warehouseRefreshMessage: "仓库缓存模拟失败");
        var messages = new FakeMessageService();
        using var viewModel = CreateViewModel(
            api, reasonDialog: new FakeReasonDialog { Result = "恢复使用" }, messages: messages, cache: cache);
        viewModel.SelectedItem = target;

        await viewModel.ActivateCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.ActivateCallCount);
        Assert.AreEqual(1, api.ActivateCommands.Single().ExpectedVersion);
        Assert.AreEqual("恢复使用", api.ActivateCommands.Single().Reason);
        Assert.AreEqual("虚拟仓已启用", messages.LastSuccessTitle);
        Assert.AreEqual("启用成功，缓存刷新失败", messages.LastWarningTitle);
        Assert.AreEqual("仓库缓存模拟失败", messages.LastWarningMessage);
        Assert.IsNull(messages.LastErrorTitle);
        Assert.AreEqual(1, cache.RefreshWarehouseSitesCallCount);
    }

    /// <summary>启用按钮只对有权限、停用选中行、无抽屉且非 Busy 的页面可用，并能及时通知状态变化。</summary>
    [TestMethod]
    public async Task ActivateCommand_ShouldTrackSelectionAndDrawerState()
    {
        using var viewModel = CreateViewModel(new FakeWarehouseApi());
        var notifications = 0;
        viewModel.ActivateCommand.CanExecuteChanged += (_, _) => notifications++;

        viewModel.SelectedItem = Warehouse("WH-A", "活动仓");
        Assert.IsFalse(viewModel.ActivateCommand.CanExecute(null));
        viewModel.SelectedItem = Warehouse("WH-I", "停用仓", isActive: false);
        Assert.IsTrue(viewModel.ActivateCommand.CanExecute(null));
        await viewModel.OpenCreateCommand.ExecuteAsync(null);
        Assert.IsFalse(viewModel.ActivateCommand.CanExecute(null));
        Assert.IsTrue(notifications > 0);
    }

    /// <summary>真实 DataGrid 选择变化会在干净编辑抽屉中加载新行详情，不要求再次点击编辑。</summary>
    [TestMethod]
    public void SelectionChanged_WhenEditingCleanRow_ShouldLoadSelectedRow()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var first = Warehouse("WH-A", "仓库甲");
            var second = Warehouse("WH-B", "仓库乙");
            var api = new FakeWarehouseApi
            {
                DetailHandler = (uid, _) => Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(uid == first.Uid ? first : second))
            };
            using var viewModel = CreateViewModel(api);
            viewModel.Items.Add(first);
            viewModel.Items.Add(second);
            viewModel.SelectedItem = first;
            await viewModel.OpenEditCommand.ExecuteAsync(null);
            var view = new WarehouseSitesView { DataContext = viewModel };
            var window = CreateHiddenWindow(view);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var grid = FindVisualDescendants<DataGrid>(view).Single();
                grid.SelectedItem = second;
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.AreSame(second, viewModel.SelectedItem);
                Assert.AreEqual(second.Uid, viewModel.EditModel?.Uid);
                Assert.IsTrue(viewModel.IsDrawerOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>脏编辑确认后才切换目标；取消确认会恢复原选择和原编辑内容。</summary>
    [TestMethod]
    public async Task SelectionChanged_WhenDirty_ShouldConfirmOrRestoreOriginalState()
    {
        var first = Warehouse("WH-A", "仓库甲");
        var second = Warehouse("WH-B", "仓库乙");
        var api = new FakeWarehouseApi
        {
            DetailHandler = (uid, _) => Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(uid == first.Uid ? first : second))
        };
        var cancelDialog = new FakeDialogService { ConfirmResult = false };
        using (var cancelled = CreateViewModel(api, dialogs: cancelDialog))
        {
            cancelled.Items.Add(first);
            cancelled.Items.Add(second);
            cancelled.SelectedItem = first;
            await cancelled.OpenEditCommand.ExecuteAsync(null);
            cancelled.EditModel!.Name = "未保存名称";
            cancelled.SelectedItem = second;

            await cancelled.HandleSelectionChangedAsync(first, second);

            Assert.AreSame(first, cancelled.SelectedItem);
            Assert.AreEqual(first.Uid, cancelled.EditModel.Uid);
            Assert.AreEqual("未保存名称", cancelled.EditModel.Name);
            Assert.AreEqual(1, cancelDialog.ConfirmCount);
        }

        var confirmDialog = new FakeDialogService { ConfirmResult = true };
        using var confirmed = CreateViewModel(api, dialogs: confirmDialog);
        confirmed.Items.Add(first);
        confirmed.Items.Add(second);
        confirmed.SelectedItem = first;
        await confirmed.OpenEditCommand.ExecuteAsync(null);
        confirmed.EditModel!.Name = "待放弃名称";
        confirmed.SelectedItem = second;

        await confirmed.HandleSelectionChangedAsync(first, second);

        Assert.AreSame(second, confirmed.SelectedItem);
        Assert.AreEqual(second.Uid, confirmed.EditModel?.Uid);
        Assert.IsFalse(confirmed.EditModel?.HasChanges);
    }

    /// <summary>未保存确认等待期间拒绝后续选行并恢复原编辑行，旧确认不得再处理已被拒绝的目标。</summary>
    [TestMethod]
    public async Task SelectionChanged_WhileDirtyConfirmationPending_ShouldRejectReentryAndKeepEditor()
    {
        var first = Warehouse("WH-A", "仓库甲");
        var second = Warehouse("WH-B", "仓库乙");
        var third = Warehouse("WH-C", "仓库丙");
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new FakeDialogService { ConfirmHandler = () => confirmation.Task };
        var api = new FakeWarehouseApi
        {
            DetailHandler = (uid, _) => Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(
                uid == first.Uid ? first : uid == second.Uid ? second : third))
        };
        using var viewModel = CreateViewModel(api, dialogs: dialogs);
        foreach (var item in new[] { first, second, third }) viewModel.Items.Add(item);
        viewModel.SelectedItem = first;
        await viewModel.OpenEditCommand.ExecuteAsync(null);
        var originalEditor = viewModel.EditModel!;
        originalEditor.Name = "未保存名称";
        viewModel.SelectedItem = second;
        var firstChange = viewModel.HandleSelectionChangedAsync(first, second);
        await Task.Yield();
        viewModel.SelectedItem = third;

        await viewModel.HandleSelectionChangedAsync(second, third);

        Assert.AreSame(first, viewModel.SelectedItem);
        Assert.AreSame(originalEditor, viewModel.EditModel);
        Assert.AreEqual(1, dialogs.ConfirmCount);
        confirmation.SetResult(true);
        await firstChange;
        Assert.AreSame(first, viewModel.SelectedItem);
        Assert.AreSame(originalEditor, viewModel.EditModel);
        Assert.AreEqual("未保存名称", viewModel.EditModel!.Name);
    }

    /// <summary>确认等待期间关闭请求不得重入 Dialog，替换后的编辑上下文不受旧确认真假影响。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SelectionChanged_WhenContextChangesDuringConfirmation_ShouldNotClearReplacement(bool confirmed)
    {
        var first = Warehouse("WH-A", "仓库甲");
        var second = Warehouse("WH-B", "仓库乙");
        var replacement = Warehouse("WH-C", "仓库丙");
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new FakeDialogService { ConfirmHandler = () => confirmation.Task };
        var api = new FakeWarehouseApi { DetailResult = ServiceResult<WarehouseSiteDto>.Ok(first) };
        using var viewModel = CreateViewModel(api, dialogs: dialogs);
        viewModel.Items.Add(first);
        viewModel.Items.Add(second);
        viewModel.SelectedItem = first;
        await viewModel.OpenEditCommand.ExecuteAsync(null);
        viewModel.EditModel!.Name = "未保存名称";
        viewModel.SelectedItem = second;
        var changing = viewModel.HandleSelectionChangedAsync(first, second);
        await Task.Yield();

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);
        var replacementEditor = WarehouseSiteEditModel.FromDto(replacement);
        viewModel.EditModel = replacementEditor;
        viewModel.SelectedItem = replacement;
        confirmation.SetResult(confirmed);
        await changing;

        Assert.AreEqual(1, dialogs.ConfirmCount);
        Assert.IsTrue(viewModel.IsDrawerOpen);
        Assert.AreSame(replacementEditor, viewModel.EditModel);
        Assert.AreSame(replacement, viewModel.SelectedItem);
    }

    /// <summary>选择确认异常由页面协调器转换为稳定提示，不向 WPF async void 事件传播异常。</summary>
    [TestMethod]
    public async Task SelectionChanged_WhenConfirmationThrows_ShouldRestoreSelectionAndReportStableError()
    {
        var first = Warehouse("WH-A", "仓库甲");
        var second = Warehouse("WH-B", "仓库乙");
        var dialogs = new FakeDialogService
        {
            ConfirmHandler = () => Task.FromException<bool>(new InvalidOperationException("模拟 Dialog 重入异常"))
        };
        var messages = new FakeMessageService();
        var api = new FakeWarehouseApi { DetailResult = ServiceResult<WarehouseSiteDto>.Ok(first) };
        using var viewModel = CreateViewModel(api, dialogs: dialogs, messages: messages);
        viewModel.Items.Add(first);
        viewModel.Items.Add(second);
        viewModel.SelectedItem = first;
        await viewModel.OpenEditCommand.ExecuteAsync(null);
        viewModel.EditModel!.Name = "未保存名称";
        viewModel.SelectedItem = second;

        await viewModel.HandleSelectionChangedAsync(first, second);

        Assert.AreSame(first, viewModel.SelectedItem);
        Assert.AreEqual("无法切换虚拟仓", messages.LastErrorTitle);
        Assert.AreEqual("切换编辑目标失败，请重试。", messages.LastErrorMessage);
    }

    /// <summary>新增模式不被选行覆盖；编辑模式清空选择会关闭干净抽屉。</summary>
    [TestMethod]
    public async Task SelectionChanged_ShouldPreserveCreateModeAndCloseCleanEditOnEmptySelection()
    {
        var row = Warehouse("WH-A", "仓库甲");
        var api = new FakeWarehouseApi { DetailResult = ServiceResult<WarehouseSiteDto>.Ok(row) };
        using var viewModel = CreateViewModel(api);
        await viewModel.OpenCreateCommand.ExecuteAsync(null);
        viewModel.SelectedItem = row;

        await viewModel.HandleSelectionChangedAsync(null, row);

        Assert.IsTrue(viewModel.EditModel?.IsCreateMode);
        Assert.IsTrue(viewModel.IsDrawerOpen);

        await viewModel.CloseDrawerCommand.ExecuteAsync(null);
        viewModel.SelectedItem = row;
        await viewModel.OpenEditCommand.ExecuteAsync(null);
        viewModel.SelectedItem = null;
        await viewModel.HandleSelectionChangedAsync(row, null);

        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.IsNull(viewModel.EditModel);
    }

    /// <summary>保存 Busy 期间选择变化会恢复编辑行，不能让写入目标与页面选择漂移。</summary>
    [TestMethod]
    public async Task SelectionChanged_DuringSave_ShouldRestoreEditingRow()
    {
        var first = Warehouse("WH-A", "仓库甲");
        var second = Warehouse("WH-B", "仓库乙");
        var saveGate = new TaskCompletionSource<ServiceResult<WarehouseSiteDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeWarehouseApi
        {
            DetailResult = ServiceResult<WarehouseSiteDto>.Ok(first),
            UpdateHandler = (_, _) => { saveStarted.TrySetResult(); return saveGate.Task; },
            SearchHandler = (_, _) => Task.FromResult(Page(first, second))
        };
        using var viewModel = CreateViewModel(api);
        viewModel.Items.Add(first);
        viewModel.Items.Add(second);
        viewModel.SelectedItem = first;
        await viewModel.OpenEditCommand.ExecuteAsync(null);
        viewModel.EditModel!.Name = "保存中的名称";
        var saving = viewModel.SaveCommand.ExecuteAsync(null);
        await saveStarted.Task;
        viewModel.SelectedItem = second;

        await viewModel.HandleSelectionChangedAsync(first, second);

        Assert.AreSame(first, viewModel.SelectedItem);
        saveGate.SetResult(ServiceResult<WarehouseSiteDto>.Ok(first));
        await saving;
    }

    /// <summary>快速从 A 切到 B/C 时，迟到的 B 详情不得覆盖最终选中的 C。</summary>
    [TestMethod]
    public async Task SelectionChanged_ShouldRejectLateDetailResponse()
    {
        var first = Warehouse("WH-A", "仓库甲");
        var second = Warehouse("WH-B", "仓库乙");
        var third = Warehouse("WH-C", "仓库丙");
        var secondGate = new TaskCompletionSource<ServiceResult<WarehouseSiteDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeWarehouseApi
        {
            DetailHandler = (uid, _) => uid == second.Uid
                ? secondGate.Task
                : Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(uid == first.Uid ? first : third))
        };
        using var viewModel = CreateViewModel(api);
        foreach (var item in new[] { first, second, third }) viewModel.Items.Add(item);
        viewModel.SelectedItem = first;
        await viewModel.OpenEditCommand.ExecuteAsync(null);
        viewModel.SelectedItem = second;
        var loadingSecond = viewModel.HandleSelectionChangedAsync(first, second);
        await Task.Yield();
        viewModel.SelectedItem = third;

        await viewModel.HandleSelectionChangedAsync(second, third);
        secondGate.SetResult(ServiceResult<WarehouseSiteDto>.Ok(second));
        await loadingSecond;

        Assert.AreSame(third, viewModel.SelectedItem);
        Assert.AreEqual(third.Uid, viewModel.EditModel?.Uid);
    }

    /// <summary>验证同一未变更建仓意图失败重试时沿用 CommandId，成功后关闭抽屉。</summary>
    [TestMethod]
    public async Task CreateRetry_ShouldReuseCommandIdUntilSuccess()
    {
        var api = new FakeWarehouseApi();
        api.CreateHandler = command =>
        {
            api.CreateCommands.Add(command);
            return Task.FromResult(api.CreateCommands.Count == 1
                ? ServiceResult<WarehouseSiteDto>.Fail("模拟网络结果未知")
                : ServiceResult<WarehouseSiteDto>.Ok(Warehouse(command.Code, command.Name)));
        };
        var cache = new ReferenceDataCacheServiceStub();
        using var viewModel = CreateViewModel(api, cache: cache);
        await viewModel.OpenCreateCommand.ExecuteAsync(null);
        var selectedSupplier = Supplier("S-01", "供应商甲");
        viewModel.EditModel!.SelectedSupplier = selectedSupplier;
        viewModel.EditModel.Code = "WH-01";

        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(2, api.CreateCommands.Count);
        Assert.AreEqual(selectedSupplier.Uid, api.CreateCommands[0].SupplierId);
        Assert.AreEqual(api.CreateCommands[0].CommandId, api.CreateCommands[1].CommandId);
        Assert.IsFalse(viewModel.IsDrawerOpen);
        Assert.AreEqual(1, cache.RefreshWarehouseSitesCallCount, "失败写入不能刷新仓库缓存，成功写入只刷新一次。");
    }

    /// <summary>写入成功后的缓存刷新失败只给出独立警告，不能把已保存结果改报为失败。</summary>
    [TestMethod]
    public async Task Save_WhenPickerCacheRefreshFails_ShouldKeepSuccessAndWarnSeparately()
    {
        var api = new FakeWarehouseApi
        {
            CreateHandler = command => Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(Warehouse(command.Code, command.Name)))
        };
        var cache = new ReferenceDataCacheServiceStub(
            warehouseRefreshSuccess: false,
            warehouseRefreshMessage: "仓库缓存模拟失败");
        var messages = new FakeMessageService();
        using var viewModel = CreateViewModel(api, messages: messages, cache: cache);
        await viewModel.OpenCreateCommand.ExecuteAsync(null);
        viewModel.EditModel!.SelectedSupplier = Supplier("S-01", "供应商甲");
        viewModel.EditModel.Code = "WH-01";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual("已创建虚拟仓", messages.LastSuccessTitle);
        Assert.AreEqual("保存成功，缓存刷新失败", messages.LastWarningTitle);
        Assert.AreEqual("仓库缓存模拟失败", messages.LastWarningMessage);
        Assert.IsNull(messages.LastErrorTitle);
        Assert.AreEqual(1, cache.RefreshWarehouseSitesCallCount);
    }

    /// <summary>验证被新查询替代的旧响应即使忽略取消，也不能覆盖最新列表。</summary>
    [TestMethod]
    public async Task Search_ShouldIgnoreLateResponseFromOlderRequest()
    {
        var oldGate = new TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSiteDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeWarehouseApi
        {
            SearchHandler = (request, _) => request.Keyword switch
            {
                "旧" => oldGate.Task,
                "新" => Task.FromResult(Page(Warehouse("NEW", "新结果"))),
                _ => Task.FromResult(Page())
            }
        };
        using var viewModel = CreateViewModel(api);
        await viewModel.InitializeAsync();
        viewModel.Keyword = "旧";
        var oldSearch = viewModel.SearchCommand.ExecuteAsync(null);
        await Task.Yield();
        viewModel.Keyword = "新";
        var newSearch = viewModel.SearchCommand.ExecuteAsync(null);
        await newSearch;
        oldGate.SetResult(Page(Warehouse("OLD", "旧结果")));
        await oldSearch;

        Assert.AreEqual(1, viewModel.Items.Count);
        Assert.AreEqual("NEW", viewModel.Items[0].Code);
    }

    private static WarehouseSitesViewModel CreateViewModel(
        FakeWarehouseApi api,
        FakeDialogService? dialogs = null,
        FakeReasonDialog? reasonDialog = null,
        IReadOnlyList<string>? permissions = null,
        FakeMessageService? messages = null,
        IReferenceDataCacheService? cache = null)
        => new(
            api,
            new PassThroughApiCallRunner(),
            messages ?? new FakeMessageService(),
            dialogs ?? new FakeDialogService(),
            reasonDialog ?? new FakeReasonDialog(),
            new CurrentUserStub(permissions ?? [PermissionKeys.InventoryWarehouseManage]),
            cache ?? new ReferenceDataCacheServiceStub());

    private static SupplierDto Supplier(string code, string name) => new()
    {
        Uid = Guid.NewGuid(), SupplierCode = code, SupplierName = name
    };

    /// <summary>创建不会干扰桌面的离屏测试窗口。</summary>
    private static Window CreateHiddenWindow(UIElement content) => new()
    {
        Width = 900,
        Height = 640,
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = -10000,
        Top = -10000,
        Content = content
    };

    private static WarehouseSiteDto Warehouse(string code, string name, bool isActive = true) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, Code = code, Name = name,
        SupplierId = Guid.NewGuid(), SupplierCode = "S-01", SupplierName = "供应商甲", IsActive = isActive
    };

    private static ServiceResult<PagedResponseBase<WarehouseSiteDto>> Page(params WarehouseSiteDto[] items)
        => ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Ok(new PagedResponseBase<WarehouseSiteDto>
        {
            Items = items.ToList(), TotalCount = items.Length, PageIndex = 1, PageSize = 20
        });

    /// <summary>遍历已加载的真实视觉树，定位抽屉内控件及其绑定结果。</summary>
    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class FakeWarehouseApi : IWarehouseSiteApi
    {
        public Func<WarehouseSiteSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>>> SearchHandler { get; init; }
            = (_, _) => Task.FromResult(Page());
        public Func<CreateSupplierVirtualWarehouseCommand, Task<ServiceResult<WarehouseSiteDto>>>? CreateHandler { get; set; }
        public Func<Guid, CancellationToken, Task<ServiceResult<WarehouseSiteDto>>>? DetailHandler { get; set; }
        public Func<Guid, UpdateWarehouseSiteCommand, Task<ServiceResult<WarehouseSiteDto>>>? UpdateHandler { get; set; }
        public Func<Guid, ActivateWarehouseSiteCommand, Task<ServiceResult<WarehouseSiteDto>>>? ActivateHandler { get; set; }
        public ServiceResult<WarehouseSiteDto> DetailResult { get; init; } = ServiceResult<WarehouseSiteDto>.Fail("未配置详情");
        public List<CreateSupplierVirtualWarehouseCommand> CreateCommands { get; } = [];
        public List<ActivateWarehouseSiteCommand> ActivateCommands { get; } = [];
        public List<WarehouseSiteSearchRequest> SearchRequests { get; } = [];
        public int SearchCallCount { get; private set; }
        public int SupplierSearchCallCount { get; private set; }
        public int ActivateCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }

        public Task<ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
        {
            SearchCallCount++;
            SearchRequests.Add(request);
            return SearchHandler(request, cancellationToken);
        }
        public Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
        {
            SupplierSearchCallCount++;
            return Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>.Ok(new PagedResponseBase<WarehouseSupplierLookupDto>
            {
                Items = [], TotalCount = 0, PageIndex = 1, PageSize = 20
            }));
        }
        public Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
            => DetailHandler?.Invoke(uid, cancellationToken) ?? Task.FromResult(DetailResult);
        public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default)
            => CreateHandler?.Invoke(command) ?? Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail("未配置创建"));
        public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
            => UpdateHandler?.Invoke(uid, command) ?? throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> ActivateAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
        {
            ActivateCallCount++;
            ActivateCommands.Add(command);
            return ActivateHandler?.Invoke(uid, command)
                ?? Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail("未配置启用"));
        }
        public Task<ServiceResult<WarehouseSiteDto>> DeleteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            return Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(Warehouse("DELETED", "已删除")));
        }
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败")
            => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; init; } = true;
        public Func<Task<bool>>? ConfirmHandler { get; init; }
        public int ConfirmCount { get; private set; }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return ConfirmHandler is null ? ConfirmResult : await ConfirmHandler();
        }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeReasonDialog : IWarehouseSiteReasonDialogService
    {
        public string? Result { get; init; }
        public string? LastAction { get; private set; }
        public Task<string?> RequestAsync(string action, WarehouseSiteDto warehouse, CancellationToken cancellationToken = default)
        {
            LastAction = action;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeMessageService : IMessageService
    {
        public string? LastSuccessTitle { get; private set; }
        public string? LastWarningTitle { get; private set; }
        public string? LastWarningMessage { get; private set; }
        public string? LastErrorTitle { get; private set; }
        public string? LastErrorMessage { get; private set; }
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) => LastSuccessTitle = title;
        public void ShowWarning(string title, string message)
        {
            LastWarningTitle = title;
            LastWarningMessage = message;
        }
        public void ShowError(string title, string message)
        {
            LastErrorTitle = title;
            LastErrorMessage = message;
        }
    }

    private sealed class CurrentUserStub(IReadOnlyList<string> permissions) : ICurrentUserService
    {
        public event EventHandler? CurrentUserChanged { add { } remove { } }
        public bool IsAuthenticated => true;
        public MeDto? CurrentUser => null;
        public string DisplayName => "测试用户";
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions { get; } = permissions;
        public bool IsAdmin => false;
        public bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey, StringComparer.Ordinal);
        public void SetCurrentUser(MeDto user) { }
        public void Clear() { }
    }
}
