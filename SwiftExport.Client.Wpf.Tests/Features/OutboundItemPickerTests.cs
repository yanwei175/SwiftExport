using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Features.Inventory.Outbounds.Services;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证库存选择的仓库、排重、默认数量和异步隔离，不连接真实 API。</summary>
[TestClass]
public sealed partial class OutboundItemPickerTests
{
    /// <summary>未选仓库不能获取；确认带入余额，重复获取不能覆盖用户编辑，已有明细禁改仓。</summary>
    [TestMethod]
    public async Task Manager_RequiresWarehouse_AndPreservesQuantity()
    {
        var source = new PickerStub();
        using var vm = new OutboundManagerViewModel(new WarehouseStub(), source, new DialogStub(), postingUser: OutboundTestUsers.Create());
        vm.AddNewHeaderRowCommand.Execute(null);
        await vm.OpenOutboundItemDialogCommand.ExecuteAsync(null);
        Assert.AreEqual(0, source.Calls);
        var h = vm.SelectedItem!;
        h.WarehouseSiteId = Guid.NewGuid();
        source.Result = [new() { WarehouseSiteId = h.WarehouseSiteId, ProductId = Guid.NewGuid(), Sku = "A", Unit = "PCS", OnHandQty = 3.125m }];
        await vm.OpenOutboundItemDialogCommand.ExecuteAsync(null);
        Assert.IsNull(h.Lines.Items.Single().Quantity, "库存余额不得默认成为手工出库数量。");
        Assert.AreEqual(3.125m, h.Lines.Items.Single().OnHandQty);
        Assert.IsNotNull(h.Lines.Items.Single().ValidateRow(), "未人工填写数量时不能保存。");
        Assert.IsFalse(vm.OpenWarehousePickerCommand.CanExecute(h));
        h.Lines.Items[0].Quantity = 1;
        await vm.OpenOutboundItemDialogCommand.ExecuteAsync(null);
        Assert.AreEqual(1, h.Lines.Items.Count);
        Assert.AreEqual(1m, h.Lines.Items[0].Quantity);
        CollectionAssert.AreEqual(new[] { source.Result[0].ProductId }, source.Request!.ExcludedProductIds.ToArray());
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
    }

    /// <summary>查询固定仓库及排除集合；过期结果和释放后的结果不得覆盖当前页。</summary>
    [TestMethod]
    public async Task Picker_LatestRequestWins_AndRejectsOtherWarehouse()
    {
        var pending = new Queue<TaskCompletionSource<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>>>();
        var requests = new List<OutboundItemSearchRequest>();
        var api = DispatchProxy.Create<IOutboundManagerApi, ApiProxy>();
        ((ApiProxy)(object)api).Handler = request =>
        {
            requests.Add(request);
            var next = new TaskCompletionSource<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>>();
            pending.Enqueue(next); return next.Task;
        };
        using var vm = new OutboundItemPickerViewModel(api, new Runner());
        var warehouse = Guid.NewGuid(); var excluded = Guid.NewGuid();
        var first = vm.InitializeAsync(new() { WarehouseSiteId = warehouse, ExcludedProductIds = [excluded] }, CancellationToken.None);
        var old = pending.Dequeue();
        var second = vm.SearchCommand.ExecuteAsync(null); var current = pending.Dequeue();
        current.SetResult(Result(warehouse, "NEW")); await second;
        old.SetResult(Result(warehouse, "OLD")); await first;
        Assert.AreEqual("NEW", vm.Items.Single().Sku);
        vm.Items[0].IsChecked = true;
        await vm.PageChangeCommand.ExecuteAsync(new SwiftExportClient.Resources.Controls.PageChangeRequest { NewIndex = 2, NewSize = 50 });
        Assert.AreEqual(0, pending.Count);
        vm.ClearSelectionCommand.Execute(null);
        var page = vm.PageChangeCommand.ExecuteAsync(new SwiftExportClient.Resources.Controls.PageChangeRequest { NewIndex = 2, NewSize = 50 });
        Assert.AreEqual(2, requests.Last().PageIndex);
        pending.Dequeue().SetResult(Result(warehouse, "NEW")); await page;
        Assert.AreEqual(2, vm.PageIndex);
        Assert.IsTrue(requests.All(x => x.WarehouseSiteId == warehouse && x.ExcludedProductIds.SequenceEqual(new[] { excluded })));
        var wrong = vm.SearchCommand.ExecuteAsync(null);
        pending.Dequeue().SetResult(Result(Guid.NewGuid(), "WRONG")); await wrong;
        Assert.AreEqual("NEW", vm.Items.Single().Sku);
        var closing = vm.SearchCommand.ExecuteAsync(null); var late = pending.Dequeue();
        vm.Dispose(); late.SetResult(Result(warehouse, "LATE")); await closing;
        Assert.AreEqual("NEW", vm.Items.Single().Sku);
        Assert.IsFalse(vm.IsPrimaryButtonEnabled);
    }

    /// <summary>当前页勾选阻止静默换页；取消和空库存均不可确认。</summary>
    [TestMethod]
    public void Selection_AndQuantityValidation()
    {
        using var vm = new OutboundItemPickerViewModel();
        vm.LoadItems([new() { OnHandQty = 2.1234m }]);
        vm.ToggleAllCommand.Execute(null);
        Assert.IsTrue(vm.IsPrimaryButtonEnabled);
        Assert.AreEqual(0m, vm.Items[0].SelectedQuantity);
        Assert.AreEqual(2.1234m, vm.Items[0].RemainingQuantity);
        vm.ClearSelectionCommand.Execute(null);
        Assert.IsFalse(vm.IsPrimaryButtonEnabled);
        vm.LoadItems([]); Assert.IsFalse(vm.IsPrimaryButtonEnabled);
        var row = new OutboundLineModel();
        row.Load(new() { ProductId = Guid.NewGuid(), UnitSnapshot = "PCS", Quantity = 5, OnHandQty = 0 });
        Assert.IsNull(row.ValidateRow());
        row.OnHandQty = 1; Assert.IsFalse(row.IsDirty);
        row.Quantity = 1.00001m; Assert.IsNotNull(row.ValidateRow());
        row.Quantity = 1.2345m; Assert.IsNull(row.ValidateRow());
    }

    /// <summary>真实选择器模板中复选框双向写回，只改变 UI 勾选。</summary>
    [TestMethod]
    public void View_CheckboxBinding()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            TestInfrastructure.ApplicationResourceLoader.Load();
            var view = new SwiftExportClient.Features.Inventory.Outbounds.Views.OutboundItemPickerView();
            var grid = ((System.Windows.Controls.Grid)view.Content).Children.OfType<System.Windows.Controls.DataGrid>().Single();
            var column = (System.Windows.Controls.DataGridTemplateColumn)grid.Columns[0];
            var box = (System.Windows.Controls.CheckBox)column.CellTemplate.LoadContent();
            var row = new OutboundItemPickerRowModel { OnHandQty = 7 };
            box.DataContext = row;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            box.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)!.UpdateTarget();
            box.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(row.IsChecked); Assert.AreEqual(7m, row.OnHandQty);
        });
    }

    private static ServiceResult<PagedResponseBase<OutboundItemCandidateDto>> Result(Guid warehouse, string sku) => ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>.Ok(new()
    { Items = [new() { Uid = Guid.NewGuid(), WarehouseSiteId = warehouse, ProductId = Guid.NewGuid(), Sku = sku, Unit = "PCS", OnHandQty = 10 }], TotalCount = 1 });
    /// <summary>只模拟 Picker 查询，意外调用写接口立即失败。</summary>
    public class ApiProxy : DispatchProxy
    {
        public Func<OutboundItemSearchRequest, Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>>> Handler { get; set; } = null!;
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name == "SearchItemsAsync" ? Handler((OutboundItemSearchRequest)args![0]!) : throw new NotSupportedException();
    }
    /// <summary>测试直接运行请求，允许模拟忽略取消的传输。</summary>
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken token, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => throw new NotSupportedException();
    }
    /// <summary>无真实仓库弹窗。</summary>
    private sealed class WarehouseStub : IWarehousePickerService
    { public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default) => Task.FromResult<WarehouseSitePickerDto?>(null); }
    /// <summary>记录选择输入并返回冻结候选。</summary>
    private sealed class PickerStub : IOutboundItemDialogService
    {
        public int Calls;
        public OutboundItemPickerRequest? Request;
        public IReadOnlyList<OutboundItemCandidateDto>? Result;
        public Task<IReadOnlyList<OutboundItemCandidateDto>?> PickAsync(OutboundItemPickerRequest request, CancellationToken cancellationToken = default)
        { Calls++; Request = request; return Task.FromResult(Result); }
    }
    /// <summary>测试默认拒绝丢弃未保存修改。</summary>
    private sealed class DialogStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
