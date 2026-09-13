using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
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
        using var viewModel = CreateViewModel(new FakeWarehouseApi(), permissions: [PermissionKeys.ModuleInventoryAccess]);

        Assert.AreEqual("inventory-warehouse-sites", WarehouseSitesViewModel.RouteKey);
        Assert.IsTrue(viewModel.IsReadOnly);
        Assert.IsFalse(viewModel.HasManagePermission);
        Assert.IsFalse(viewModel.OpenCreateCommand.CanExecute(null));
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
                Assert.AreEqual(warehouse.SupplierCode, display.Text);
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
        using var viewModel = CreateViewModel(api);
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

    private static WarehouseSiteDto Warehouse(string code, string name) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, Code = code, Name = name,
        SupplierId = Guid.NewGuid(), SupplierCode = "S-01", SupplierName = "供应商甲", IsActive = true
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
        public ServiceResult<WarehouseSiteDto> DetailResult { get; init; } = ServiceResult<WarehouseSiteDto>.Fail("未配置详情");
        public List<CreateSupplierVirtualWarehouseCommand> CreateCommands { get; } = [];
        public List<WarehouseSiteSearchRequest> SearchRequests { get; } = [];
        public int SearchCallCount { get; private set; }
        public int SupplierSearchCallCount { get; private set; }
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
            => Task.FromResult(DetailResult);
        public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default)
            => CreateHandler?.Invoke(command) ?? Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail("未配置创建"));
        public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
        public int ConfirmCount { get; private set; }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeReasonDialog : IWarehouseSiteReasonDialogService
    {
        public string? Result { get; init; }
        public Task<string?> RequestAsync(string action, WarehouseSiteDto warehouse, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeMessageService : IMessageService
    {
        public string? LastErrorTitle { get; private set; }
        public string? LastErrorMessage { get; private set; }
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
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
