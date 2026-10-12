using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Suppliers.Components;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

/// <summary>验证缓存选择器接入供应商产品工作区后的初始化、选择和未保存变更保护。</summary>
[TestClass]
[DoNotParallelize]
public sealed class SupplierProductWorkspaceTests
{
    /// <summary>完整缓存不受原远程搜索二十条限制，确认后沿用编辑器的稳定身份查询。</summary>
    [TestMethod]
    public async Task SelectSupplier_ShouldUseCompleteCacheAndOpenEditorForPersistedSupplier()
    {
        var options = Enumerable.Range(1, 25)
            .Select(index => Supplier(Guid.NewGuid(), $"S{index:000}", $"供应商{index}"))
            .ToArray();
        var cache = new ReferenceDataCacheServiceStub(suppliers: options);
        var productApi = new SupplierProductApiStub();
        using var workspace = CreateWorkspace(cache, productApi, new DialogServiceStub());

        Assert.IsFalse(workspace.Editor.IsOpen);
        Assert.IsEmpty(productApi.Requests);

        await workspace.InitializeAsync();
        await workspace.InitializeAsync();

        Assert.AreEqual(1, cache.EnsureLoadedCallCount, "同一页面实例不得重复初始化缓存。");
        Assert.AreSame(cache.Suppliers, workspace.SupplierOptions);
        Assert.HasCount(25, workspace.SupplierOptions);
        Assert.IsNull(workspace.SelectedSupplier);
        Assert.IsEmpty(productApi.Requests, "加载候选不得自动选择或查询产品。");

        var selected = options[^1];
        workspace.SelectedSupplier = selected;
        await workspace.CommitSupplierCommand.ExecuteAsync(null);

        Assert.AreSame(selected, workspace.SelectedSupplier);
        Assert.IsTrue(workspace.Editor.IsOpen);
        Assert.AreEqual(selected.Uid, workspace.Editor.ActiveSupplier?.Uid);
        Assert.IsFalse(workspace.Editor.ActiveSupplier!.IsNew);
        Assert.HasCount(1, productApi.Requests);
        Assert.AreEqual(selected.Uid, productApi.Requests[0].SupplierId);
        Assert.AreEqual("供应商产品管理 - S025", workspace.DocumentTitle);
        Assert.AreEqual(selected.Currency, workspace.Editor.ActiveSupplier.Currency);
        Assert.AreEqual(selected.TaxRate, workspace.Editor.ActiveSupplier.TaxRate);
    }

    /// <summary>缓存加载失败只反馈一次，并保留缓存服务已有快照供本地选择。</summary>
    [TestMethod]
    public async Task Initialize_WhenCacheFails_ShouldWarnOnceAndPreserveOptions()
    {
        var option = Supplier(Guid.NewGuid(), "S001", "供应商一");
        var cache = new ReferenceDataCacheServiceStub(suppliers: [option], ensureSuccess: false);
        var messages = new MessageServiceStub();
        using var workspace = CreateWorkspace(cache, new SupplierProductApiStub(), new DialogServiceStub(), messages);

        await workspace.InitializeAsync();
        await workspace.InitializeAsync();

        Assert.AreEqual(1, messages.Warnings.Count);
        Assert.AreSame(option, workspace.SupplierOptions.Single());
        Assert.IsFalse(workspace.Editor.IsOpen);
    }

    /// <summary>取消切换或关闭时保留原编辑器模型、缓存 DTO 和全部未保存变更。</summary>
    [TestMethod]
    public async Task SwitchOrCloseWithDirtyEditor_WhenDiscardRejected_ShouldRestoreActiveSupplier()
    {
        var first = Supplier(Guid.NewGuid(), "S001", "供应商一");
        var second = Supplier(Guid.NewGuid(), "S002", "供应商二");
        var dialog = new DialogServiceStub { ConfirmationResult = false };
        var productApi = new SupplierProductApiStub();
        using var workspace = CreateWorkspace(
            new ReferenceDataCacheServiceStub(suppliers: [first, second]), productApi, dialog);

        workspace.SelectedSupplier = first;
        await workspace.CommitSupplierCommand.ExecuteAsync(null);
        var active = workspace.Editor.ActiveSupplier;
        workspace.Editor.Products.AddNewItem(first.Uid, first.SupplierCode, first.SupplierName, first.Currency, first.TaxRate);
        workspace.SelectedSupplier = second;

        await workspace.CommitSupplierCommand.ExecuteAsync(null);

        Assert.AreSame(first, workspace.SelectedSupplier);
        Assert.AreSame(active, workspace.Editor.ActiveSupplier);
        Assert.HasCount(1, productApi.Requests);
        Assert.IsFalse(await workspace.CanCloseAsync(CancellationToken.None));
        Assert.IsTrue(workspace.HasUnsavedChanges);
    }

    /// <summary>清空和释放仅关闭当前工作区，不清除其他页面共用的候选缓存。</summary>
    [TestMethod]
    public async Task ClearAndDispose_ShouldPreserveSharedCache()
    {
        var first = Supplier(Guid.NewGuid(), "S001", "供应商一");
        var cache = new ReferenceDataCacheServiceStub(suppliers: [first]);
        using var workspace = CreateWorkspace(cache, new SupplierProductApiStub(), new DialogServiceStub());
        await workspace.InitializeAsync();
        workspace.SelectedSupplier = first;
        await workspace.CommitSupplierCommand.ExecuteAsync(null);

        workspace.SelectedSupplier = null;
        await workspace.ClearSupplierCommand.ExecuteAsync(null);

        Assert.IsNull(workspace.SelectedSupplier);
        Assert.IsFalse(workspace.Editor.IsOpen);
        Assert.IsFalse(workspace.HasUnsavedChanges);
        workspace.Dispose();
        workspace.Dispose();
        await workspace.InitializeAsync();
        Assert.AreEqual(1, cache.EnsureLoadedCallCount);
        Assert.AreSame(first, cache.Suppliers.Single());
    }

    /// <summary>通过真实双向 Binding 和键盘确认验证 DTO 提交、拒绝切换回填及清空命令。</summary>
    [TestMethod]
    public void SelectorBinding_ShouldCommitRestoreAndClearWorkspace()
    {
        StaTest.RunAsync(async () =>
        {
            var first = Supplier(Guid.NewGuid(), "S001", "供应商一");
            var second = Supplier(Guid.NewGuid(), "S002", "供应商二");
            var dialog = new DialogServiceStub { ConfirmationResult = false };
            using var workspace = CreateWorkspace(
                new ReferenceDataCacheServiceStub(suppliers: [first, second]), new SupplierProductApiStub(), dialog);
            var control = new SupplierAutoSuggestComboBox { DataContext = workspace };
            control.SetBinding(SupplierAutoSuggestComboBox.ItemsSourceProperty, new Binding(nameof(workspace.SupplierOptions)));
            control.SetBinding(SupplierAutoSuggestComboBox.SelectedSupplierProperty,
                new Binding(nameof(workspace.SelectedSupplier)) { Mode = BindingMode.TwoWay });
            control.SetBinding(SupplierAutoSuggestComboBox.SelectionCommittedCommandProperty,
                new Binding(nameof(workspace.CommitSupplierCommand)));
            control.SetBinding(SupplierAutoSuggestComboBox.SelectionClearedCommandProperty,
                new Binding(nameof(workspace.ClearSupplierCommand)));
            var window = new Window { Content = control, Width = 400, Height = 120, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
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

                Assert.AreSame(first, control.SelectedSupplier);
                Assert.AreSame(first, workspace.SelectedSupplier);
                Assert.AreEqual(first.Uid, workspace.Editor.ActiveSupplier?.Uid);
                workspace.Editor.Products.AddNewItem(first.Uid, first.SupplierCode, first.SupplierName, first.Currency, first.TaxRate);

                dropDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dropDown));
                results.SelectedItem = second;
                RaiseEnter(input);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.AreSame(first, control.SelectedSupplier, "拒绝切换后强类型选择必须回填到控件。");
                Assert.AreSame(first, workspace.SelectedSupplier);
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clear));
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.AreSame(first, control.SelectedSupplier, "拒绝清空后必须恢复原选择。");
                Assert.IsTrue(workspace.HasUnsavedChanges);

                dialog.ConfirmationResult = true;
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clear));
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsNull(control.SelectedSupplier);
                Assert.IsNull(workspace.SelectedSupplier);
                Assert.IsFalse(workspace.Editor.IsOpen);
                Assert.HasCount(2, workspace.SupplierOptions);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>向连接到窗口的输入框发送 Enter，沿用 LookupTextBox 的实际键盘确认路径。</summary>
    private static void RaiseEnter(UIElement input) => input.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });

    /// <summary>用离线产品 API、缓存和弹窗结果组装本次工作区测试。</summary>
    private static SupplierProductWorkspaceViewModel CreateWorkspace(
        IReferenceDataCacheService cache,
        ISupplierProductApi productApi,
        IDialogService dialogService,
        IMessageService? messages = null)
    {
        var runner = new DirectApiCallRunner();
        messages ??= new MessageServiceStub();
        var editor = new SupplierProductManagerViewModel(
            productApi, null!, runner, messages, dialogService);
        return new SupplierProductWorkspaceViewModel(cache, messages, editor);
    }

    /// <summary>构造已持久化的完整供应商候选，供选择器和编辑器共享稳定身份。</summary>
    private static SupplierDto Supplier(Guid uid, string code, string name) => new()
    {
        Uid = uid,
        Version = 1,
        SupplierCode = code,
        SupplierName = name,
        DocRelateNo = $"DOC-{code}",
        Currency = "CNY",
        TaxRate = 13m,
    };

    private sealed class SupplierProductApiStub : ISupplierProductApi
    {
        public List<SupplierProductSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync(
            SupplierProductSearchRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(
                new PagedResponseBase<SupplierProductDto>
                {
                    Items = [],
                    TotalCount = 0,
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                }));
        }

        public Task<ServiceResult<SupplierProductDto>> GetByIdAsync(Guid id) =>
            NotSupported<SupplierProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(SupplierProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SupplierProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync(
            SupplierProductBatchSaveRequest request) =>
            NotSupported<SupplierProductBatchSaveResponse>();
        public Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(
            SetProductDefaultSupplierCommand command) =>
            NotSupported<SetProductDefaultSupplierResult>();
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
