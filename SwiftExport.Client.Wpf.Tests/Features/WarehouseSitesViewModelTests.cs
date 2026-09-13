using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Inventory.WarehouseSites.Components;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Views;
using SwiftExportClient.Services.Api;
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

    /// <summary>验证抽屉在切换到编辑模型后仍通过页面代理解析候选数据和页面级命令。</summary>
    [TestMethod]
    public void DrawerBindings_ShouldResolvePageViewModelThroughProxy()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            using var viewModel = CreateViewModel(new FakeWarehouseApi());
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

                var editor = FindVisualDescendants<WarehouseSupplierLookupTextBox>(view)
                    .Single(candidate => BindingOperations
                        .GetBinding(candidate, WarehouseSupplierLookupTextBox.SelectedSupplierProperty)
                        ?.Path.Path == "SelectedSupplier");
                Assert.AreSame(viewModel.EditSupplierCandidates, editor.ItemsSource);
                Assert.AreSame(viewModel.SearchEditSupplierCommand, editor.SearchCommand);
                Assert.AreEqual(viewModel.IsEditSupplierBusy, editor.IsBusy);

                var buttons = FindVisualDescendants<Button>(view).ToList();
                Assert.IsTrue(buttons.Any(button => Equals(button.Content, "保存") && ReferenceEquals(button.Command, viewModel.SaveCommand)));
                Assert.AreEqual(2, buttons.Count(button => Equals(button.Content, "取消") && ReferenceEquals(button.Command, viewModel.CloseDrawerCommand)));

                listener.Flush();
                var bindingErrors = output.ToString();
                Assert.IsFalse(bindingErrors.Contains("EditSupplierCandidates", StringComparison.Ordinal), bindingErrors);
                Assert.IsFalse(bindingErrors.Contains("SearchEditSupplierCommand", StringComparison.Ordinal), bindingErrors);
                Assert.IsFalse(bindingErrors.Contains("IsEditSupplierBusy", StringComparison.Ordinal), bindingErrors);
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
        viewModel.EditModel!.SelectedSupplier = new WarehouseSupplierLookupDto
        {
            Uid = Guid.NewGuid(), SupplierCode = "S-01", SupplierName = "供应商甲"
        };
        viewModel.EditModel.Code = "WH-01";

        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(2, api.CreateCommands.Count);
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
        IReadOnlyList<string>? permissions = null)
        => new(
            api,
            new PassThroughApiCallRunner(),
            new FakeMessageService(),
            dialogs ?? new FakeDialogService(),
            reasonDialog ?? new FakeReasonDialog(),
            new CurrentUserStub(permissions ?? [PermissionKeys.InventoryWarehouseManage]));

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
        public int DeleteCallCount { get; private set; }

        public Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
            => SearchHandler(request, cancellationToken);
        public Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>.Ok(new PagedResponseBase<WarehouseSupplierLookupDto>
            {
                Items = [], TotalCount = 0, PageIndex = 1, PageSize = 20
            }));
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
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
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
