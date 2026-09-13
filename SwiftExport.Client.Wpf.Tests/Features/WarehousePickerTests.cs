using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Views;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory.WarehouseSites;

/// <summary>验证仓库选择器的单选结果、分页清选、请求竞态、关闭生命周期和真实 DataGrid 绑定。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WarehousePickerTests
{
    /// <summary>PageNavigator 协议换页时清除旧页选择，只有当前页重新选择后才能确认。</summary>
    [TestMethod]
    public async Task NextPage_ShouldClearStaleSelectionAndRequireCurrentRow()
    {
        var first = Warehouse("WH-01");
        var second = Warehouse("WH-02");
        var cache = new ReferenceDataCacheServiceStub(warehouseSites: [first, second]);
        using var picker = new WarehousePickerViewModel(cache) { PageSize = 1 };

        await picker.OpenAsync();
        var dialogPrimaryEnabled = false;
        picker.StateChanged += (_, _) => dialogPrimaryEnabled = picker.IsPrimaryButtonEnabled;
        picker.SelectedItem = first;
        Assert.IsTrue(picker.IsPrimaryButtonEnabled);
        Assert.IsTrue(dialogPrimaryEnabled, "选择当前行后必须通知公共外壳启用确定按钮。");

        picker.PageChangeCommand.Execute(new SwiftExportClient.Resources.Controls.PageChangeRequest
        {
            OldIndex = 1, NewIndex = 2, OldSize = 1, NewSize = 1
        });

        Assert.IsNull(picker.SelectedItem);
        Assert.IsFalse(picker.IsPrimaryButtonEnabled);
        Assert.IsFalse(dialogPrimaryEnabled, "换页清选后必须通知公共外壳禁用确定按钮。");
        Assert.AreSame(second, picker.Items.Single());
    }

    /// <summary>搜索只在完整仓库快照中本地匹配代码、名称和供应商，并重置分页选择。</summary>
    [TestMethod]
    public async Task Search_ShouldFilterLocalCacheAndClearSelection()
    {
        var first = Warehouse("WH-A");
        var second = Warehouse("WH-B");
        second.SupplierName = "目标供应商";
        var cache = new ReferenceDataCacheServiceStub(warehouseSites: [first, second]);
        using var picker = new WarehousePickerViewModel(cache);
        await picker.OpenAsync();
        picker.SelectedItem = first;
        picker.Keyword = "目标";

        picker.SearchCommand.Execute(null);

        Assert.IsNull(picker.SelectedItem);
        Assert.AreSame(second, picker.Items.Single());
        Assert.AreEqual(1, picker.PageIndex);
    }

    /// <summary>关闭会话后，未完成响应不得再写入集合或生成可确认结果。</summary>
    [TestMethod]
    public async Task Close_ShouldRejectLateResponse()
    {
        var gate = new TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var picker = new WarehousePickerViewModel(CreateBlockingCache(gate));
        var loading = picker.OpenAsync();
        await Task.Yield();

        picker.Dispose();
        gate.SetResult(Page(Warehouse("LATE")));
        await loading;

        Assert.AreEqual(0, picker.Items.Count);
        Assert.IsNull(picker.GetSelectedResult());
    }

    /// <summary>新筛选失败时，上一轮候选不能继续被选择或确认。</summary>
    [TestMethod]
    public async Task FailedReplacementSearch_ShouldInvalidatePreviousCandidates()
    {
        var previous = Warehouse("WH-A");
        var cache = new ReferenceDataCacheServiceStub(
            warehouseSites: [previous], warehouseRefreshSuccess: false, warehouseRefreshMessage: "模拟失败");
        using var picker = new WarehousePickerViewModel(cache);
        await picker.OpenAsync();
        picker.SelectedItem = previous;
        await picker.RefreshCommand.ExecuteAsync(null);

        Assert.AreEqual(1, picker.Items.Count);
        Assert.AreSame(previous, picker.Items.Single());
        Assert.AreEqual(1, picker.PageIndex);
        Assert.IsFalse(picker.IsPrimaryButtonEnabled);
        Assert.IsNull(picker.GetSelectedResult());
        Assert.AreEqual("模拟失败", picker.ResultSummary);
    }

    /// <summary>Feature 服务仅接纳主按钮确认；关闭按钮明确返回空值。</summary>
    [TestMethod]
    public void Service_ShouldReturnSelectedOnPrimaryAndNullOnCancel()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var warehouse = Warehouse("WH-01");
            var cache = new ReferenceDataCacheServiceStub(warehouseSites: [warehouse]);
            var primaryDialog = new DialogStub(DialogResult.Primary, state => ((WarehousePickerViewModel)state!).SelectedItem = warehouse);
            var selected = await new WarehousePickerService(() => new WarehousePickerViewModel(cache), primaryDialog).PickAsync();

            var cancelled = await new WarehousePickerService(
                () => new WarehousePickerViewModel(cache), new DialogStub(DialogResult.Close)).PickAsync();

            Assert.AreSame(warehouse, selected);
            Assert.IsNull(cancelled);
            Assert.IsInstanceOfType<WarehousePickerView>(primaryDialog.Content);
        });
    }

    /// <summary>同一 Feature 服务每次选择创建独立 ViewModel，不复用已释放会话。</summary>
    [TestMethod]
    public void Service_ShouldCreateFreshViewModelForEveryPick()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var warehouse = Warehouse("WH-01");
            var cache = new ReferenceDataCacheServiceStub(warehouseSites: [warehouse]);
            var created = new List<WarehousePickerViewModel>();
            var dialog = new DialogStub(DialogResult.Close);
            var service = new WarehousePickerService(() =>
            {
                var picker = new WarehousePickerViewModel(cache);
                created.Add(picker);
                return picker;
            }, dialog);

            await service.PickAsync();
            await service.PickAsync();

            Assert.HasCount(2, created);
            Assert.AreNotSame(created[0], created[1]);
        });
    }

    /// <summary>真实视图保持只读整行单选，并把 API 候选绑定到 DataGrid。</summary>
    [TestMethod]
    public void View_ShouldBindReadOnlySingleRowDataGrid()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var warehouse = Warehouse("WH-01");
            using var picker = new WarehousePickerViewModel(
                new ReferenceDataCacheServiceStub(warehouseSites: [warehouse]));
            await picker.OpenAsync();
            var view = new WarehousePickerView { DataContext = picker };
            var window = new Window
            {
                Width = 900, Height = 600, ShowInTaskbar = false, ShowActivated = false,
                Left = -10000, Top = -10000, Content = view
            };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                var grid = (DataGrid)view.FindName("WarehouseGrid")!;
                Assert.IsTrue(grid.IsReadOnly);
                Assert.AreEqual(DataGridSelectionMode.Single, grid.SelectionMode);
                Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
                Assert.AreSame(picker.Items, grid.ItemsSource);
                Assert.AreEqual(1, grid.Items.Count);
                var navigator = FindVisualDescendant<SwiftExportClient.Resources.Controls.PageNavigator>(view);
                Assert.IsNotNull(navigator);
                Assert.AreSame(picker.PageChangeCommand, navigator.PageChangeCommand);

                var closeCount = 0;
                picker.CloseRequested += (_, args) =>
                {
                    Assert.AreEqual(DialogResult.Primary, args.Result);
                    closeCount++;
                };
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = grid
                });
                Assert.AreEqual(0, closeCount, "表头、空白或 Grid 自身双击不能确认旧选择。");

                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(warehouse);
                row.IsSelected = true;
                var cell = FindVisualDescendant<DataGridCell>(row);
                Assert.IsNotNull(cell);
                cell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = cell
                });
                Assert.AreEqual(1, closeCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static WarehouseSitePickerDto Warehouse(string code) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, Code = code, Name = code + "名称",
        SupplierId = Guid.NewGuid(), SupplierCode = "S-01", SupplierName = "供应商甲", IsActive = true
    };

    private static ServiceResult<PagedResponseBase<WarehouseSitePickerDto>> Page(
        WarehouseSitePickerDto item, int pageIndex = 1, int totalCount = 1)
        => ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Ok(new PagedResponseBase<WarehouseSitePickerDto>
        {
            Items = [item], TotalCount = totalCount, PageIndex = pageIndex, PageSize = 1
        });

    /// <summary>创建只实现本测试所需仓库缓存成员的动态替身。</summary>
    private static IReferenceDataCacheService CreateBlockingCache(
        TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> gate)
    {
        var source = new ObservableCollection<WarehouseSitePickerDto>();
        var readOnly = new ReadOnlyObservableCollection<WarehouseSitePickerDto>(source);
        var proxy = DispatchProxy.Create<IReferenceDataCacheService, CacheProxy>();
        ((CacheProxy)(object)proxy).Handler = (method, _) => method.Name switch
        {
            "get_WarehouseSites" => readOnly,
            nameof(IReferenceDataCacheService.EnsureWarehouseSitesLoadedAsync) => CompleteAsync(),
            _ => throw new NotSupportedException(method.Name)
        };
        return proxy;

        async Task<CacheRefreshResult> CompleteAsync()
        {
            var result = await gate.Task;
            if (result.Success && result.Data is not null)
                foreach (var item in result.Data.Items) source.Add(item);
            return CreateCacheResult(result.Success, result.Message);
        }
    }

    private static CacheRefreshResult CreateCacheResult(bool success, string message) =>
        (CacheRefreshResult)(Activator.CreateInstance(
            typeof(CacheRefreshResult),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [success, message],
            culture: null) ?? throw new InvalidOperationException("无法创建缓存测试结果。"));

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindVisualDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private sealed class WarehouseApiStub : IWarehouseSiteApi
    {
        public Func<PagedRequestBase, CancellationToken, Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>> Handler { get; init; }
            = (_, _) => Task.FromResult(Page(Warehouse("WH-01")));
        public Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => Handler(request, cancellationToken);
        public Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeleteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    private sealed class CacheProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private sealed class DialogStub(DialogResult result, Action<IDialogContentState?>? beforeReturn = null) : IDialogService
    {
        public object? Content { get; private set; }
        public Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default)
        {
            Content = content;
            beforeReturn?.Invoke(state);
            return Task.FromResult(result);
        }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingDialogStub(WarehouseSitePickerDto selection) : IDialogService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DialogResult> ShowAsync(object content, DialogOptions options, IDialogContentState? state = null, CancellationToken cancellationToken = default)
        {
            ((WarehousePickerViewModel)state!).SelectedItem = selection;
            Entered.SetResult();
            await Release.Task;
            return DialogResult.Primary;
        }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
