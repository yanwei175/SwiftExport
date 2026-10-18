using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Apis.ProductApis;
using System.Reflection;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Navigation;
using SwiftExportClient.Features.Customers.CustomerProducts.Views;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExportClient.Features.Customers.Components;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证缓存选择器接入客户产品工作区后的初始化、选择和未保存变更保护。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CustomerProductManagerTests
{
    /// <summary>已有产品数据后切换客户，新请求等待期间旧客户行和旧状态提示立即退出。</summary>
    [TestMethod]
    public async Task SwitchingLoadedCustomer_ShouldClearPreviousRowsBeforeAwait()
    {
        var first = Customer(Guid.NewGuid(), "A", "客户一"); var second = Customer(Guid.NewGuid(), "B", "客户二");
        var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerProductDto>>>();
        var api = new CustomerProductApiStub { Read = request => request.CustomerId == first.Uid ? Task.FromResult(Page(first, "INITIAL")) : pending.Task };
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(customers: [first, second]), api, new DialogServiceStub());
        vm.SelectedCustomer = first; await vm.CommitCustomerCommand.ExecuteAsync(null); Assert.HasCount(1, vm.Products.Items);
        vm.SelectedCustomer = second; var loading = vm.CommitCustomerCommand.ExecuteAsync(null);
        Assert.IsEmpty(vm.Products.Items); Assert.AreEqual("客户产品管理 - B", vm.DocumentTitle); StringAssert.Contains(vm.StatusText, "客户 B");
        pending.SetResult(Page(second, "LATEST")); await loading; Assert.AreEqual(second.Uid, vm.Products.Items.Single().CustomerId);
    }

    /// <summary>真实合并页面的未提交数值错误阻断切换、清空及关闭，卸载和重载对称桥接编辑回调。</summary>
    [TestMethod]
    public void ActualPage_PendingInputGuardsAndReloadLifecycle() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var first = Customer(Guid.NewGuid(), "A", "客户一"); var second = Customer(Guid.NewGuid(), "B", "客户二");
        var api = new CustomerProductApiStub();
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(customers: [first, second]), api, new DialogServiceStub());
        vm.SelectedCustomer = first; await vm.CommitCustomerCommand.ExecuteAsync(null);
        var row = vm.Products.AddNewItem(first.Uid, first.CustomerCode); row.CustomerSku = "DRAFT";
        var view = new CustomerProductManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1300, Height = 750, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName("ProductGrid"); var price = grid.Columns.OfType<DataGridTextColumn>().Single(column => Equals(column.Header, "美元价"));
            grid.ScrollIntoView(row, price); grid.UpdateLayout(); grid.CurrentCell = new(row, price); Assert.IsTrue(grid.BeginEdit());
            var editor = (TextBox)price.GetCellContent(row); editor.Text = "INVALID"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsFalse(vm.CommitPendingEdits!()); Assert.IsFalse(await vm.CanCloseAsync(default));
            vm.SelectedCustomer = second; await vm.CommitCustomerCommand.ExecuteAsync(null); await vm.ClearCustomerCommand.ExecuteAsync(null);
            Assert.AreSame(first, vm.SelectedCustomer); Assert.AreEqual(first.Uid, vm.ActiveCustomer!.Uid); Assert.AreSame(row, vm.Products.Items.Single());
            Assert.HasCount(1, api.Requests); editor.Text = "12.5"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(vm.CommitPendingEdits!()); Assert.AreEqual(12.5m, row.UsdPrice);
            window.Content = null; await Dispatcher.Yield(DispatcherPriority.Background); Assert.IsNull(vm.CommitPendingEdits);
            window.Content = view; await Dispatcher.Yield(DispatcherPriority.Loaded); Assert.IsNotNull(vm.CommitPendingEdits);
            Assert.AreSame(row, vm.Products.Items.Single()); Assert.IsTrue(vm.HasUnsavedChanges);
        }
        finally { window.Close(); }
        await Dispatcher.Yield(DispatcherPriority.Background); Assert.IsNull(vm.CommitPendingEdits);
    });

    /// <summary>Lookup 即使忽略取消并晚到，页面释放后也不能重建候选或覆盖选择。</summary>
    [TestMethod]
    public async Task Dispose_ShouldRejectLateLookupResponse()
    {
        var pending = new TaskCompletionSource<ServiceResult<List<ProductLookupDto>>>();
        var lookup = DispatchProxy.Create<IProductApi, LookupApiProxy>(); ((LookupApiProxy)(object)lookup).Response = pending.Task;
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(), new CustomerProductApiStub(), new DialogServiceStub(), productLookup: lookup);
        var searching = vm.SearchProductsCommand.ExecuteAsync("SKU"); vm.Dispose();
        pending.SetResult(ServiceResult<List<ProductLookupDto>>.Ok([new() { Uid = Guid.NewGuid(), Sku = "LATE" }])); await searching;
        Assert.IsEmpty(vm.ProductCandidates); Assert.IsNull(vm.SelectedCandidate);
    }

    /// <summary>只提供 Lookup 等待任务，其他 API 不属于本生命周期测试。</summary>
    public class LookupApiProxy : DispatchProxy
    {
        /// <summary>由测试决定响应完成时机。</summary>
        public Task<ServiceResult<List<ProductLookupDto>>> Response { get; set; } = null!;
        /// <summary>保证测试只允许产品候选查询。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name == nameof(IProductApi.SearchLookupAsync)
            ? Response : throw new AssertFailedException(targetMethod.Name);
    }

    /// <summary>快速客户切换替代旧查询，新标题不配旧数据，忽略取消的旧响应也不能回写。</summary>
    [TestMethod]
    public async Task RapidCustomerSwitch_ShouldLoadLatestCustomerAndRejectOldResponse()
    {
        var first = Customer(Guid.NewGuid(), "A", "客户一"); var second = Customer(Guid.NewGuid(), "B", "客户二");
        var older = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerProductDto>>>();
        var newer = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerProductDto>>>();
        var api = new CustomerProductApiStub { Read = request => request.CustomerId == first.Uid ? older.Task : newer.Task };
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(customers: [first, second]), api, new DialogServiceStub());
        vm.SelectedCustomer = first; var a = vm.CommitCustomerCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsLoading);
        vm.SelectedCustomer = second; var b = vm.CommitCustomerCommand.ExecuteAsync(null);
        Assert.HasCount(2, api.Requests); Assert.AreEqual(second.Uid, vm.ActiveCustomer!.Uid); Assert.IsEmpty(vm.Products.Items);
        Assert.AreEqual("客户产品管理 - B", vm.DocumentTitle);
        newer.SetResult(Page(second, "LATEST")); await b;
        older.SetResult(Page(first, "OLD")); await a;
        Assert.AreEqual(second.Uid, vm.Products.Items.Single().CustomerId); Assert.AreEqual("LATEST", vm.Products.Items.Single().CustomerSku);
        Assert.AreSame(second, vm.SelectedCustomer); Assert.IsFalse(vm.IsLoading);
    }

    /// <summary>释放取消查询等待并解除产品集合订阅，迟到结果及外部集合变化不再修改文档状态。</summary>
    [TestMethod]
    public async Task Dispose_ShouldInvalidatePendingReadAndDetachProductState()
    {
        var customer = Customer(Guid.NewGuid(), "A", "客户一");
        var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerProductDto>>>();
        var api = new CustomerProductApiStub { Read = _ => pending.Task };
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(customers: [customer]), api, new DialogServiceStub());
        vm.SelectedCustomer = customer; var loading = vm.CommitCustomerCommand.ExecuteAsync(null);
        vm.Dispose(); await loading; pending.SetResult(Page(customer, "LATE"));
        Assert.IsEmpty(vm.Products.Items); Assert.IsNull(vm.CommitPendingEdits);
        vm.Products.AddNewItem(customer.Uid, customer.CustomerCode);
        Assert.IsFalse(vm.IsDirty, "释放后已解除 Products.PropertyChanged，不再转发集合草稿。");
    }

    /// <summary>保存期间切换、清空和页签关闭均被拒绝，客户身份与未提交产品保持一致。</summary>
    [TestMethod]
    public async Task Saving_ShouldBlockCustomerSwitchClearAndTabClose()
    {
        var first = Customer(Guid.NewGuid(), "A", "客户一"); var second = Customer(Guid.NewGuid(), "B", "客户二");
        var pending = new TaskCompletionSource<ServiceResult<CustomerProductBatchSaveResponse>>();
        CustomerProductBatchSaveRequest? sent = null;
        var api = new CustomerProductApiStub { Save = request => { sent = request; return pending.Task; } };
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(customers: [first, second]), api, new DialogServiceStub());
        vm.SelectedCustomer = first; await vm.CommitCustomerCommand.ExecuteAsync(null);
        var row = vm.Products.AddNewItem(first.Uid, first.CustomerCode); row.CustomerSku = "DRAFT";
        var save = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy); Assert.IsFalse(vm.CanChangeCustomer);
        vm.SelectedCustomer = second; await vm.CommitCustomerCommand.ExecuteAsync(null); await vm.ClearCustomerCommand.ExecuteAsync(null);
        Assert.IsFalse(await vm.CanCloseAsync(default)); Assert.AreSame(first, vm.SelectedCustomer); Assert.AreEqual(first.Uid, vm.ActiveCustomer!.Uid);
        Assert.AreSame(row, vm.Products.Items.Single()); Assert.HasCount(1, api.Requests);
        pending.SetResult(ServiceResult<CustomerProductBatchSaveResponse>.Ok(new()
        { Items = [new() { Success = true, ClientKey = sent!.CreateItems.Single().ClientKey, Uid = Guid.NewGuid(), NewVersion = 1, Operation = SaveOperation.Create }] }));
        await save; Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>导航页签直接监听唯一 VM 的标题和草稿，拒绝清空保留状态，确认清空恢复空标题。</summary>
    [TestMethod]
    public async Task Tab_ShouldFollowManagerTitleDirtyAndClearGuards()
    {
        var customer = Customer(Guid.NewGuid(), "A", "客户一"); var dialog = new DialogServiceStub { ConfirmationResult = false };
        using var vm = CreateWorkspace(new ReferenceDataCacheServiceStub(customers: [customer]), new CustomerProductApiStub(), dialog);
        var registry = new NavigationRouteRegistry(); Assert.IsTrue(registry.TryGetRoute(CustomerProductManagerViewModel.RouteKey, out var route));
        using var tab = new NavigationTabItem(route, vm);
        vm.SelectedCustomer = customer; await vm.CommitCustomerCommand.ExecuteAsync(null);
        Assert.AreEqual("客户产品管理 - A", tab.Title);
        vm.Products.AddNewItem(customer.Uid, customer.CustomerCode); Assert.IsTrue(tab.IsDirty);
        await vm.ClearCustomerCommand.ExecuteAsync(null); Assert.IsTrue(tab.IsDirty); Assert.AreSame(customer, vm.SelectedCustomer);
        dialog.ConfirmationResult = true; await vm.ClearCustomerCommand.ExecuteAsync(null);
        Assert.AreEqual("客户产品管理", tab.Title); Assert.IsFalse(tab.IsDirty); Assert.IsFalse(vm.IsOpen);
    }

    /// <summary>为并发验证返回指定客户的独立产品页，不复制生产分页算法。</summary>
    private static ServiceResult<PagedResponseBase<CustomerProductDto>> Page(CustomerDto customer, string sku) =>
        ServiceResult<PagedResponseBase<CustomerProductDto>>.Ok(new()
        { Items = [new() { Uid = Guid.NewGuid(), CustomerId = customer.Uid, CustomerCode = customer.CustomerCode, CustomerSku = sku }], TotalCount = 1, PageIndex = 1, PageSize = 20 });

    /// <summary>完整缓存不受原远程搜索二十条限制，确认后沿用编辑器的稳定身份查询。</summary>
    [TestMethod]
    public async Task SelectCustomer_ShouldUseCompleteCacheAndOpenEditorForPersistedCustomer()
    {
        var options = Enumerable.Range(1, 25)
            .Select(index => Customer(Guid.NewGuid(), $"C{index:000}", $"客户{index}"))
            .ToArray();
        var cache = new ReferenceDataCacheServiceStub(customers: options);
        var productApi = new CustomerProductApiStub();
        using var workspace = CreateWorkspace(cache, productApi, new DialogServiceStub());

        Assert.IsFalse(workspace.IsOpen);
        Assert.IsEmpty(productApi.Requests);

        await workspace.InitializeAsync();
        await workspace.InitializeAsync();

        Assert.AreEqual(1, cache.EnsureLoadedCallCount, "同一页面实例不得重复初始化缓存。");
        Assert.AreSame(cache.Customers, workspace.CustomerOptions);
        Assert.HasCount(25, workspace.CustomerOptions);
        Assert.IsNull(workspace.SelectedCustomer);
        Assert.IsEmpty(productApi.Requests, "加载候选不得自动选择或查询产品。");

        var selected = options[^1];
        workspace.SelectedCustomer = selected;
        await workspace.CommitCustomerCommand.ExecuteAsync(null);

        Assert.AreSame(selected, workspace.SelectedCustomer);
        Assert.IsTrue(workspace.IsOpen);
        Assert.AreEqual(selected.Uid, workspace.ActiveCustomer?.Uid);
        Assert.IsFalse(workspace.ActiveCustomer!.IsNew);
        Assert.HasCount(1, productApi.Requests);
        Assert.AreEqual(selected.Uid, productApi.Requests[0].CustomerId);
        Assert.AreEqual("客户产品管理 - C025", workspace.DocumentTitle);
    }

    /// <summary>缓存加载失败只反馈一次，并保留缓存服务已有快照供本地选择。</summary>
    [TestMethod]
    public async Task Initialize_WhenCacheFails_ShouldWarnOnceAndPreserveOptions()
    {
        var option = Customer(Guid.NewGuid(), "C001", "客户一");
        var cache = new ReferenceDataCacheServiceStub(customers: [option], ensureSuccess: false);
        var messages = new MessageServiceStub();
        using var workspace = CreateWorkspace(cache, new CustomerProductApiStub(), new DialogServiceStub(), messages);

        await workspace.InitializeAsync();
        await workspace.InitializeAsync();

        Assert.AreEqual(1, messages.Warnings.Count);
        Assert.AreSame(option, workspace.CustomerOptions.Single());
        Assert.IsFalse(workspace.IsOpen);
    }

    /// <summary>取消切换或关闭时保留原编辑器模型、缓存 DTO 和全部未保存变更。</summary>
    [TestMethod]
    public async Task SwitchOrCloseWithDirtyEditor_WhenDiscardRejected_ShouldRestoreActiveCustomer()
    {
        var first = Customer(Guid.NewGuid(), "C001", "客户一");
        var second = Customer(Guid.NewGuid(), "C002", "客户二");
        var dialog = new DialogServiceStub { ConfirmationResult = false };
        var productApi = new CustomerProductApiStub();
        using var workspace = CreateWorkspace(
            new ReferenceDataCacheServiceStub(customers: [first, second]), productApi, dialog);

        workspace.SelectedCustomer = first;
        await workspace.CommitCustomerCommand.ExecuteAsync(null);
        var active = workspace.ActiveCustomer;
        workspace.Products.AddNewItem(first.Uid, first.CustomerCode);
        workspace.SelectedCustomer = second;

        await workspace.CommitCustomerCommand.ExecuteAsync(null);

        Assert.AreSame(first, workspace.SelectedCustomer);
        Assert.AreSame(active, workspace.ActiveCustomer);
        Assert.HasCount(1, productApi.Requests);
        Assert.IsFalse(await workspace.CanCloseAsync(CancellationToken.None));
        Assert.IsTrue(workspace.HasUnsavedChanges);
    }

    /// <summary>清空和释放仅关闭当前工作区，不清除其他页面共用的候选缓存。</summary>
    [TestMethod]
    public async Task ClearAndDispose_ShouldPreserveSharedCache()
    {
        var first = Customer(Guid.NewGuid(), "C001", "客户一");
        var cache = new ReferenceDataCacheServiceStub(customers: [first]);
        using var workspace = CreateWorkspace(cache, new CustomerProductApiStub(), new DialogServiceStub());
        await workspace.InitializeAsync();
        workspace.SelectedCustomer = first;
        await workspace.CommitCustomerCommand.ExecuteAsync(null);

        workspace.SelectedCustomer = null;
        await workspace.ClearCustomerCommand.ExecuteAsync(null);

        Assert.IsNull(workspace.SelectedCustomer);
        Assert.IsFalse(workspace.IsOpen);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        workspace.Dispose();
        workspace.Dispose();
        await workspace.InitializeAsync();
        Assert.AreEqual(1, cache.EnsureLoadedCallCount);
        Assert.AreSame(first, cache.Customers.Single());
    }

    /// <summary>通过真实双向 Binding 和键盘确认验证 DTO 提交、拒绝切换回填及清空命令。</summary>
    [TestMethod]
    public void SelectorBinding_ShouldCommitRestoreAndClearWorkspace()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var first = Customer(Guid.NewGuid(), "C001", "客户一");
            var second = Customer(Guid.NewGuid(), "C002", "客户二");
            var dialog = new DialogServiceStub { ConfirmationResult = false };
            using var workspace = CreateWorkspace(
                new ReferenceDataCacheServiceStub(customers: [first, second]), new CustomerProductApiStub(), dialog);
            var view = new CustomerProductManagerView { DataContext = workspace };
            var control = (CustomerAutoSuggestComboBox)view.FindName("CustomerSelector");
            var window = new Window { Content = view, Width = 1300, Height = 750, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                Assert.AreSame(workspace, control.DataContext);
                Assert.AreSame(workspace, ((DataGrid)view.FindName("ProductGrid")).DataContext);
                Assert.AreEqual("清空客户", ((Button)view.FindName("ClearCustomerButton")).Content);
                Assert.IsNotNull(workspace.CommitPendingEdits);
                var editor = (LookupTextBox)control.FindName("LookupEditor");
                var input = (TextBox)editor.FindName("InputTextBox");
                var results = (ListBox)editor.FindName("ResultList");
                var dropDown = (Button)editor.FindName("BtnDropDown");
                var clear = (Button)editor.FindName("BtnClear");
                input.Focus();
                Keyboard.Focus(input);

                dropDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dropDown));
                results.SelectedItem = first;
                RaiseEnter(input);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.AreSame(first, control.SelectedCustomer);
                Assert.AreSame(first, workspace.SelectedCustomer);
                Assert.AreEqual(first.Uid, workspace.ActiveCustomer?.Uid);
                workspace.Products.AddNewItem(first.Uid, first.CustomerCode);

                dropDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dropDown));
                results.SelectedItem = second;
                RaiseEnter(input);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.AreSame(first, control.SelectedCustomer, "拒绝切换后强类型选择必须回填到控件。");
                Assert.AreSame(first, workspace.SelectedCustomer);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clear));
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.AreSame(first, control.SelectedCustomer, "拒绝清空后必须恢复原选择。");
                Assert.IsTrue(workspace.HasUnsavedChanges);

                dialog.ConfirmationResult = true;
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clear));
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsNull(control.SelectedCustomer);
                Assert.IsNull(workspace.SelectedCustomer);
                Assert.IsFalse(workspace.IsOpen);
                Assert.HasCount(2, workspace.CustomerOptions);
            }
            finally
            {
                window.Close();
            }
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsNull(workspace.CommitPendingEdits, "页面卸载后释放原生编辑提交回调。");
        });
    }

    /// <summary>向连接到窗口的输入框发送 Enter，沿用 LookupTextBox 的实际键盘确认路径。</summary>
    private static void RaiseEnter(UIElement input) => input.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    /// <summary>用离线产品 API、缓存和弹窗结果组装本次工作区测试。</summary>
    private static CustomerProductManagerViewModel CreateWorkspace(
        IReferenceDataCacheService cache,
        ICustomerProductApi productApi,
        IDialogService dialogService,
        IMessageService? messages = null, IProductApi? productLookup = null)
    {
        var runner = new DirectApiCallRunner();
        messages ??= new MessageServiceStub();
        return new CustomerProductManagerViewModel(
            productApi, productLookup!, runner, messages, dialogService, cache);
    }

    /// <summary>构造已持久化的完整客户候选，供选择器和编辑器共享稳定身份。</summary>
    private static CustomerDto Customer(Guid uid, string code, string name) => new()
    {
        Uid = uid,
        Version = 1,
        CustomerCode = code,
        CustomerName = name,
    };

    private sealed class CustomerProductApiStub : ICustomerProductApi
    {
        public Func<CustomerProductSearchRequest, Task<ServiceResult<PagedResponseBase<CustomerProductDto>>>>? Read;
        public Func<CustomerProductBatchSaveRequest, Task<ServiceResult<CustomerProductBatchSaveResponse>>>? Save;
        public List<CustomerProductSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest request)
        {
            Requests.Add(request);
            if (Read is not null) return Read(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<CustomerProductDto>>.Ok(
                new PagedResponseBase<CustomerProductDto>
                {
                    Items = [],
                    TotalCount = 0,
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                }));
        }

        public Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid id) => NotSupported<CustomerProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(CustomerProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, CustomerProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<List<CustomerDto>>> GetAllHaveProductCustomerList() =>
            NotSupported<List<CustomerDto>>();
        public Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(Guid customerId, string sku) =>
            NotSupported<CustomerProductDto>();
        public Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(
            CustomerProductBatchSaveRequest request) =>
            Save?.Invoke(request) ?? NotSupported<CustomerProductBatchSaveResponse>();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            string failureMessage = "请求失败") =>
            action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") =>
            action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") =>
            ServiceResult<T>.Ok(await action(), successMessage);
    }

    /// <summary>记录缓存加载警告，产品查询出现错误时使离线测试失败。</summary>
    private sealed class MessageServiceStub : IMessageService
    {
        public List<string> Warnings { get; } = [];
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) => Warnings.Add(message);
        public void ShowError(string title, string message) =>
            Assert.Fail($"{title}: {message}");
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public bool ConfirmationResult { get; set; } = true;

        public Task ShowContentAsync(
            string title,
            object content,
            string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfirmationResult);

        public Task ShowErrorAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static Task<ServiceResult<T>> NotSupported<T>() =>
        Task.FromException<ServiceResult<T>>(new NotSupportedException());
}
