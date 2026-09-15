using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.Converters;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.Views;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory;

/// <summary>验证库存只读页面的 Trace 身份隔离与离页生命周期。</summary>
[TestClass]
public sealed class InventoryReadOnlyViewModelTests
{
    /// <summary>库存流水枚举值保持兼容，并为每个成员提供约定的中文说明。</summary>
    [TestMethod]
    public void TransactionTypes_ShouldKeepPublishedValuesAndChineseDescriptions()
    {
        var expected = new Dictionary<InventoryTransactionTypeEnum, (int Value, string Description)>
        {
            [InventoryTransactionTypeEnum.PurchaseReceiptInbound] = (10, "采购入库"),
            [InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound] = (11, "客供件入库"),
            [InventoryTransactionTypeEnum.PackingListAutoReceiptInbound] = (12, "装箱单自动入库"),
            [InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound] = (20, "客户发货出库"),
            [InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound] = (21, "客供件消耗出库"),
            [InventoryTransactionTypeEnum.StockIssueOutbound] = (22, "普通库存出库"),
            [InventoryTransactionTypeEnum.NegativeStockExceptionOutbound] = (90, "负库存异常出库"),
            [InventoryTransactionTypeEnum.OpeningBalance] = (100, "期初余额"),
            [InventoryTransactionTypeEnum.Reversal] = (900, "反向流水")
        };

        Assert.AreEqual(expected.Count, Enum.GetValues<InventoryTransactionTypeEnum>().Length);
        foreach (var pair in expected)
        {
            Assert.AreEqual(pair.Value.Value, (int)pair.Key);
            var member = typeof(InventoryTransactionTypeEnum).GetField(pair.Key.ToString())!;
            Assert.AreEqual(pair.Value.Description, member.GetCustomAttributes(typeof(DescriptionAttribute), false).Cast<DescriptionAttribute>().Single().Description);
        }
        CollectionAssert.AreEqual(expected.Keys.ToArray(), InventoryTransactionTypeList.All.ToArray());
    }

    /// <summary>来源类型转换器只改变页面展示，未知值必须保留原文便于追溯。</summary>
    [TestMethod]
    public void SourceDocumentTypeConverter_ShouldDisplayKnownBlankAndUnknownValues()
    {
        var converter = new InventorySourceDocumentTypeDisplayConverter();

        Assert.AreEqual("入库单", converter.Convert("InboundDocument", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("出库单", converter.Convert("OutboundDocument", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("未记录", converter.Convert(" ", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("未知来源（LegacyImport）", converter.Convert("LegacyImport", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>流水页使用共享分页协议，换页携带新页参数，重新查询回到第一页。</summary>
    [TestMethod]
    public async Task Ledger_PageChangeAndSearch_ShouldUseRequestedPageAndResetToFirstPage()
    {
        var inventory = new InventoryApiStub();
        using var viewModel = new InventoryLedgerViewModel(inventory, new WarehouseApiStub(), new Runner(), new ReferenceDataCacheServiceStub());
        await viewModel.InitializeAsync();

        await viewModel.PageChangeCommand.ExecuteAsync(new PageChangeRequest { OldIndex = 1, NewIndex = 3, OldSize = 50, NewSize = 100 });
        Assert.AreEqual(3, inventory.LastLedgerRequest!.PageIndex);
        Assert.AreEqual(100, inventory.LastLedgerRequest.PageSize);

        viewModel.Keyword = "SKU-1";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(1, inventory.LastLedgerRequest.PageIndex);
        Assert.AreEqual(100, inventory.LastLedgerRequest.PageSize);
        Assert.AreEqual("SKU-1", inventory.LastLedgerRequest.Keyword);
    }

    /// <summary>流水页关闭后，已经发出的延迟查询不得回写列表。</summary>
    [TestMethod]
    public async Task Ledger_DisposeDuringSearch_ShouldRejectLateResponse()
    {
        var inventory = new InventoryApiStub();
        using var viewModel = new InventoryLedgerViewModel(inventory, new WarehouseApiStub(), new Runner(), new ReferenceDataCacheServiceStub());
        await viewModel.InitializeAsync();
        inventory.DelayedLedgerResult = new TaskCompletionSource<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var loading = viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.Dispose();
        inventory.DelayedLedgerResult.SetResult(ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Ok(new PagedResponseBase<InventoryLedgerItemDto>
        {
            Items = [new InventoryLedgerItemDto { Uid = Guid.NewGuid() }], TotalCount = 1, PageIndex = 1, PageSize = 50
        }));
        await loading;

        Assert.AreEqual(0, viewModel.Items.Count);
    }

    /// <summary>流水视图绑定共享分页控件、中文枚举和局部来源转换器。</summary>
    [TestMethod]
    public void LedgerView_ShouldBindSharedPagingAndDisplayConverters()
    {
        var view = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Client.Wpf", "Features", "Inventory", "Ledger", "Views", "InventoryLedgerView.xaml"));
        StringAssert.Contains(view, "controls:PageNavigator");
        StringAssert.Contains(view, "PageChangeCommand=\"{Binding PageChangeCommand}\"");
        StringAssert.Contains(view, "TransactionType,Converter={StaticResource EnumDescriptionConverter}");
        StringAssert.Contains(view, "SourceDocumentType,Converter={StaticResource InventorySourceDocumentTypeDisplayConverter}");
    }

    /// <summary>交易类型下拉必须把 null 选项显示为“全部”，并允许从具体类型切回 null。</summary>
    [TestMethod]
    public void LedgerView_TransactionTypeFilter_ShouldSelectVisibleAllOption()
    {
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            using var viewModel = new InventoryLedgerViewModel(new InventoryApiStub(), new WarehouseApiStub(), new Runner(), new ReferenceDataCacheServiceStub());
            var view = new InventoryLedgerView { DataContext = viewModel };
            var window = new Window
            {
                Width = 1200, Height = 700, ShowInTaskbar = false, ShowActivated = false,
                Left = -10000, Top = -10000, Content = view
            };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                var filter = FindVisualDescendants<ComboBox>(view)
                    .Single(combo => BindingOperations.GetBinding(combo, ComboBox.ItemsSourceProperty)?.Path.Path == nameof(viewModel.TransactionTypes));

                filter.IsDropDownOpen = true;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                filter.UpdateLayout();
                var allItem = (ComboBoxItem?)filter.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(allItem);
                Assert.IsTrue(FindVisualDescendants<TextBlock>(allItem).Any(text => text.Text == "全部"));

                filter.SelectedIndex = 1;
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert.AreEqual(InventoryTransactionTypeEnum.PurchaseReceiptInbound, viewModel.TransactionType);

                filter.SelectedIndex = 0;
                filter.IsDropDownOpen = false;
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                filter.UpdateLayout();
                Assert.IsNull(viewModel.TransactionType);
                Assert.IsTrue(FindVisualDescendants<TextBlock>(filter).Any(text => text.Text == "全部"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>切换余额后，旧行的延迟 Trace 响应不得展示到新选中行。</summary>
    [TestMethod]
    public async Task BalanceTrace_SwitchSelection_ShouldRejectPreviousResponse()
    {
        var api = new InventoryApiStub();
        using var viewModel = new InventoryBalancesViewModel(api, new WarehouseApiStub(), new Runner(), new MessageStub(), new ReferenceDataCacheServiceStub());
        var first = new InventoryBalanceDto { Uid = Guid.NewGuid() };
        var second = new InventoryBalanceDto { Uid = Guid.NewGuid() };
        viewModel.SelectedItem = first;

        var loading = viewModel.LoadTraceCommand.ExecuteAsync(null);
        viewModel.SelectedItem = second;
        api.TraceResult.SetResult(ServiceResult<InventoryBalanceTraceDto>.Ok(new InventoryBalanceTraceDto
        {
            Balance = first,
            Ledgers = [new InventoryLedgerItemDto { Uid = Guid.NewGuid() }],
            TotalCount = 1, PageIndex = 1, PageSize = 50,
            Reconciliation = new InventoryReconciliationDto { IsReconciled = true }
        }));
        await loading;

        Assert.AreEqual(0, viewModel.TraceItems.Count);
        Assert.AreEqual("请选择加载 Trace。", viewModel.ReconciliationSummary);
        Assert.AreEqual(1, viewModel.TracePageIndex);
    }

    /// <summary>页面已经离开时 Initialize 不得再发仓库或库存查询。</summary>
    [TestMethod]
    public async Task Ledger_DisposedBeforeInitialize_ShouldNotSendRequests()
    {
        var inventory = new InventoryApiStub();
        var warehouse = new WarehouseApiStub();
        var viewModel = new InventoryLedgerViewModel(inventory, warehouse, new Runner(), new ReferenceDataCacheServiceStub());
        viewModel.Dispose();

        await viewModel.InitializeAsync();

        Assert.AreEqual(0, warehouse.SearchCount);
        Assert.AreEqual(0, inventory.LedgerSearchCount);
    }

    private sealed class InventoryApiStub : IInventoryApi
    {
        public TaskCompletionSource<ServiceResult<InventoryBalanceTraceDto>> TraceResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LedgerSearchCount { get; private set; }
        public InventoryLedgerSearchRequest? LastLedgerRequest { get; private set; }
        public TaskCompletionSource<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>>? DelayedLedgerResult { get; set; }
        public Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult<PagedResponseBase<InventoryBalanceDto>>.Ok(new PagedResponseBase<InventoryBalanceDto>()));
        public Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default)
        {
            LedgerSearchCount++;
            LastLedgerRequest = request;
            return DelayedLedgerResult?.Task ?? Task.FromResult(ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Ok(new PagedResponseBase<InventoryLedgerItemDto>
            {
                PageIndex = request.PageIndex, PageSize = request.PageSize
            }));
        }
        public Task<ServiceResult<InventoryBalanceTraceDto>> GetTraceAsync(Guid uid, int pageIndex = 1, int pageSize = 50, CancellationToken cancellationToken = default) => TraceResult.Task;
    }

    private sealed class WarehouseApiStub : IWarehouseSiteApi
    {
        public int SearchCount { get; private set; }
        public Task<ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) { SearchCount++; return Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Ok(new PagedResponseBase<WarehouseSiteDto>())); }
        public Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class MessageStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }
}
