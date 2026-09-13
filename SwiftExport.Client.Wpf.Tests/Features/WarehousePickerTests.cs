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
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory.WarehouseSites;

/// <summary>验证仓库选择器的单选结果、分页清选、请求竞态、关闭生命周期和真实 DataGrid 绑定。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WarehousePickerTests
{
    /// <summary>翻页开始时清除旧页选择，只有当前页重新选择后才能确认。</summary>
    [TestMethod]
    public async Task NextPage_ShouldClearStaleSelectionAndRequireCurrentRow()
    {
        var first = Warehouse("WH-01");
        var second = Warehouse("WH-02");
        var api = new WarehouseApiStub
        {
            Handler = (request, _) => Task.FromResult(Page(request.PageIndex == 1 ? first : second, request.PageIndex, 2))
        };
        using var picker = new WarehousePickerViewModel(api, new Runner());

        await picker.OpenAsync();
        picker.SelectedItem = first;
        Assert.IsTrue(picker.IsPrimaryButtonEnabled);

        await picker.NextPageCommand.ExecuteAsync(null);

        Assert.IsNull(picker.SelectedItem);
        Assert.IsFalse(picker.IsPrimaryButtonEnabled);
        Assert.AreSame(second, picker.Items.Single());
    }

    /// <summary>旧请求即使忽略取消并迟到，也不能覆盖最后一次搜索的结果。</summary>
    [TestMethod]
    public async Task Search_ShouldAcceptOnlyLatestResponse()
    {
        var oldGate = new TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new WarehouseApiStub
        {
            Handler = (request, _) => request.Keyword == "旧"
                ? oldGate.Task
                : Task.FromResult(Page(Warehouse("NEW")))
        };
        using var picker = new WarehousePickerViewModel(api, new Runner()) { Keyword = "旧" };
        var oldLoad = picker.OpenAsync();
        await Task.Yield();
        picker.Keyword = "新";

        await picker.SearchCommand.ExecuteAsync(null);
        oldGate.SetResult(Page(Warehouse("OLD")));
        await oldLoad;

        Assert.AreEqual("NEW", picker.Items.Single().Code);
    }

    /// <summary>关闭会话后，未完成响应不得再写入集合或生成可确认结果。</summary>
    [TestMethod]
    public async Task Close_ShouldRejectLateResponse()
    {
        var gate = new TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new WarehouseApiStub { Handler = (_, _) => gate.Task };
        using var picker = new WarehousePickerViewModel(api, new Runner());
        var loading = picker.OpenAsync();
        await Task.Yield();

        picker.Close();
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
        var api = new WarehouseApiStub
        {
            Handler = (request, _) => request.Keyword == "失败"
                ? Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Fail("模拟失败"))
                : Task.FromResult(Page(previous))
        };
        using var picker = new WarehousePickerViewModel(api, new Runner());
        await picker.OpenAsync();
        picker.SelectedItem = previous;
        picker.Keyword = "失败";

        await picker.SearchCommand.ExecuteAsync(null);

        Assert.AreEqual(0, picker.Items.Count);
        Assert.AreEqual(0, picker.TotalCount);
        Assert.AreEqual(1, picker.PageIndex);
        Assert.IsFalse(picker.IsPrimaryButtonEnabled);
        Assert.IsNull(picker.GetSelectedResult());
    }

    /// <summary>Feature 服务仅接纳主按钮确认；关闭按钮明确返回空值。</summary>
    [TestMethod]
    public void Service_ShouldReturnSelectedOnPrimaryAndNullOnCancel()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var warehouse = Warehouse("WH-01");
            var api = new WarehouseApiStub { Handler = (_, _) => Task.FromResult(Page(warehouse)) };
            using var primaryPicker = new WarehousePickerViewModel(api, new Runner());
            var primaryDialog = new DialogStub(DialogResult.Primary, state => ((WarehousePickerViewModel)state!).SelectedItem = warehouse);
            var selected = await new WarehousePickerService(primaryPicker, primaryDialog).PickAsync();

            using var cancelPicker = new WarehousePickerViewModel(api, new Runner());
            var cancelled = await new WarehousePickerService(cancelPicker, new DialogStub(DialogResult.Close)).PickAsync();

            Assert.AreSame(warehouse, selected);
            Assert.IsNull(cancelled);
            Assert.IsInstanceOfType<WarehousePickerView>(primaryDialog.Content);
        });
    }

    /// <summary>同一 Feature 服务并发打开时拒绝后来者，且后来者不会关闭首个会话。</summary>
    [TestMethod]
    public void Service_ShouldRejectReentryWithoutClosingOwnerSession()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var warehouse = Warehouse("WH-01");
            using var picker = new WarehousePickerViewModel(
                new WarehouseApiStub { Handler = (_, _) => Task.FromResult(Page(warehouse)) }, new Runner());
            var dialog = new BlockingDialogStub(warehouse);
            var service = new WarehousePickerService(picker, dialog);
            var owner = service.PickAsync();
            await dialog.Entered.Task;

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.PickAsync());
            StringAssert.Contains(exception.Message, "已经打开");
            Assert.IsTrue(picker.IsPrimaryButtonEnabled);

            dialog.Release.SetResult();
            Assert.AreSame(warehouse, await owner);
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
                new WarehouseApiStub { Handler = (_, _) => Task.FromResult(Page(warehouse)) }, new Runner());
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
