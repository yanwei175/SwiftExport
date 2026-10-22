using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.Views;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Services.Api;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证箱单管理服务端多选请求快照及真实页面 Popup，不调用网络或修改详情页。</summary>
[TestClass, DoNotParallelize]
public sealed class PackingListManagerFilterTests
{
    /// <summary>首次空缓存由初始化加载并通知真实筛选控件；重复初始化不会重复加载。</summary>
    [TestMethod]
    public void InitializeLoadsInitiallyEmptyCustomerCacheIntoView() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var customers = Customers(); var cache = new ReferenceDataCacheServiceStub();
        var ready = new TaskCompletionSource();
        cache.BeforeEnsureLoadedAsync = async () => { await ready.Task; cache.SetCustomers(customers); };
        var ui = new PackingListUiStub(); var searches = 0;
        var api = new PackingListApiStub { Search = _ => { searches++; return Task.FromResult(EmptyPage()); } };
        using var vm = new PackingListManagerViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), new PackingListNavigationStub(), ui, ui, cache);
        var view = new PackingListManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1500, Height = 850, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show(); await Idle(); Assert.HasCount(0, vm.Customers);
            var initialization = vm.InitializeAsync(); Assert.AreEqual(0, searches);
            ready.SetResult(); await initialization; await vm.InitializeAsync(); await Idle();
            Assert.AreEqual(1, cache.EnsureLoadedCallCount); Assert.AreEqual(1, searches); Assert.HasCount(0, ui.Warnings);
            var filter = (MultiSelectLookupButton)view.FindName("CustomersFilter");
            await Open(filter);
            Assert.HasCount(3, ((ListBox)filter.FindName("PART_ListBox")).Items);
            Cancel(filter);
        }
        finally { window.Close(); await Idle(); }
    });

    /// <summary>缓存业务失败或异常均给出安全提示，不能被后续成功的箱单查询吞掉。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InitializeWarnsOnCacheFailureAndStillQueries(bool throws)
    {
        var cache = new ReferenceDataCacheServiceStub(ensureSuccess: false);
        if (throws) cache.BeforeEnsureLoadedAsync = () => Task.FromException(new InvalidOperationException("内部技术信息"));
        var ui = new PackingListUiStub(); var searches = 0;
        var api = new PackingListApiStub { Search = _ => { searches++; return Task.FromResult(EmptyPage()); } };
        using var vm = new PackingListManagerViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), new PackingListNavigationStub(), ui, ui, cache);
        await vm.InitializeAsync();
        Assert.AreEqual(1, searches); Assert.HasCount(1, ui.Warnings);
        StringAssert.Contains(ui.Warnings.Single(), "客户缓存未完整加载");
        Assert.IsFalse(ui.Warnings.Single().Contains("内部技术信息"));
    }

    /// <summary>加载期间关闭页面后，不弹缓存警告，也不再启动箱单查询。</summary>
    [TestMethod]
    public async Task DisposeDuringCacheLoadRejectsInitializationContinuation()
    {
        var ready = new TaskCompletionSource();
        var cache = new ReferenceDataCacheServiceStub(ensureSuccess: false) { BeforeEnsureLoadedAsync = () => ready.Task };
        var ui = new PackingListUiStub(); var searches = 0;
        var api = new PackingListApiStub { Search = _ => { searches++; return Task.FromResult(EmptyPage()); } };
        using var vm = new PackingListManagerViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), new PackingListNavigationStub(), ui, ui, cache);
        var initialization = vm.InitializeAsync(); vm.Dispose(); ready.SetResult(); await initialization;
        Assert.AreEqual(0, searches); Assert.HasCount(0, ui.Warnings);
    }

    /// <summary>相同客户代码仍以各自 Uid 传输，控件后续清空不能修改已经构造的请求。</summary>
    [TestMethod]
    public void MappingCapturesCustomerIdentitiesAndIndependentStatusLists()
    {
        var customers = Customers(); var search = new PackingListManagerSearchModel();
        search.SelectedCustomers.Add(customers[0]); search.SelectedCustomers.Add(customers[1]);
        search.PostingStatuses.Add(PackingListPostingStatusEnum.Unposted);
        search.PostingStatuses.Add(PackingListPostingStatusEnum.Posted);
        var request = search.Adapt<PackingListManagerSearchRequest>();
        search.SelectedCustomers.Clear(); search.PostingStatuses.Clear();
        CollectionAssert.AreEqual(new[] { customers[0].Uid, customers[1].Uid }, request.CustomerIds);
        CollectionAssert.AreEqual(new[] { PackingListPostingStatusEnum.Unposted, PackingListPostingStatusEnum.Posted }, request.PostingStatuses);
        Assert.IsNull(request.CustomerId); Assert.IsNull(request.PostingStatus);
    }

    /// <summary>翻页和刷新仅复制成功应用的条件；待应用修改、旧请求及清空不能污染该快照。</summary>
    [TestMethod]
    public async Task PagingAndRefreshKeepAppliedFiltersUntilSearchAppliesClearedCollections()
    {
        var customers = Customers(); var requests = new List<PackingListManagerSearchRequest>();
        var api = new PackingListApiStub { Search = request => { requests.Add(request); return Task.FromResult(EmptyPage()); } };
        using var vm = Manager(api, customers);
        vm.Search.SelectedCustomers.Add(customers[0]); vm.Search.SelectedCustomers.Add(customers[1]);
        vm.Search.PostingStatuses.Add(PackingListPostingStatusEnum.Unposted); vm.Search.PostingStatuses.Add(PackingListPostingStatusEnum.Posted);
        vm.Search.Keyword = "已应用"; vm.Search.InvoiceDateFrom = new DateTime(2026, 10, 1);
        await vm.SearchCommand.ExecuteAsync(null);
        requests[0].CustomerIds.Clear(); requests[0].PostingStatuses.Clear();
        vm.ClearFiltersCommand.Execute(null); vm.Search.Keyword = "尚未查询";
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 3, NewSize = 50 });
        var page = requests.Last(); Assert.AreEqual(3, page.PageIndex); Assert.AreEqual(50, page.PageSize);
        CollectionAssert.AreEqual(new[] { customers[0].Uid, customers[1].Uid }, page.CustomerIds);
        CollectionAssert.AreEqual(new[] { PackingListPostingStatusEnum.Unposted, PackingListPostingStatusEnum.Posted }, page.PostingStatuses);
        Assert.AreEqual("已应用", page.Keyword); Assert.AreEqual(new DateTime(2026, 10, 1), page.InvoiceDateFrom);
        page.CustomerIds.Clear(); page.PostingStatuses.Clear();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.HasCount(2, requests.Last().CustomerIds); Assert.HasCount(2, requests.Last().PostingStatuses);
        vm.ClearFiltersCommand.Execute(null); await vm.SearchCommand.ExecuteAsync(null);
        Assert.HasCount(0, requests.Last().CustomerIds); Assert.HasCount(0, requests.Last().PostingStatuses);
        Assert.IsNull(requests.Last().Keyword); Assert.IsNull(requests.Last().InvoiceDateFrom);
        Assert.AreEqual(1, requests.Last().PageIndex); Assert.AreEqual(50, requests.Last().PageSize);
    }

    /// <summary>在途请求不受清空影响；失败的新筛选不能替换成功应用的查询范围。</summary>
    [TestMethod]
    public async Task InFlightSnapshotAndFailedSearchPreserveAcceptedFilters()
    {
        var customers = Customers(); var requests = new List<PackingListManagerSearchRequest>();
        var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>();
        var api = new PackingListApiStub { Search = request => { requests.Add(request); return pending.Task; } };
        using var vm = Manager(api, customers);
        vm.Search.SelectedCustomers.Add(customers[0]); vm.Search.PostingStatuses.Add(PackingListPostingStatusEnum.Reversed);
        var loading = vm.SearchCommand.ExecuteAsync(null); vm.ClearFiltersCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { customers[0].Uid }, requests[0].CustomerIds);
        CollectionAssert.AreEqual(new[] { PackingListPostingStatusEnum.Reversed }, requests[0].PostingStatuses);
        pending.SetResult(EmptyPage()); await loading;
        api.Search = request => { requests.Add(request); return Task.FromResult(ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>.Fail("新查询失败")); };
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual("新查询失败", vm.ValidationSummary);
        api.Search = request => { requests.Add(request); return Task.FromResult(EmptyPage()); };
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 20 });
        CollectionAssert.AreEqual(new[] { customers[0].Uid }, requests.Last().CustomerIds);
        CollectionAssert.AreEqual(new[] { PackingListPostingStatusEnum.Reversed }, requests.Last().PostingStatuses);
    }

    /// <summary>实际 Manager 双 Popup 支持中文和代码搜索、多选、Esc 取消、清除隐藏选择及按 Uid 恢复勾选。</summary>
    [TestMethod]
    public void RealManagerViewCommitsBothFiltersAndCancelsOrClearsDraftSelections() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var customers = Customers();
        var requests = new List<PackingListManagerSearchRequest>();
        var api = new PackingListApiStub { Search = request => { requests.Add(request); return Task.FromResult(EmptyPage()); } };
        using var vm = Manager(api, customers); await vm.InitializeAsync();
        var view = new PackingListManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1500, Height = 850, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show(); await Idle();
            var customerFilter = (MultiSelectLookupButton)view.FindName("CustomersFilter");
            var statusFilter = (MultiSelectLookupButton)view.FindName("PostingStatusesFilter");
            Assert.AreSame(vm.Search.SelectedCustomers, customerFilter.SelectedItems);
            Assert.AreSame(vm.Search.PostingStatuses, statusFilter.SelectedItems);
            Assert.AreEqual("客户筛选", AutomationProperties.GetName(customerFilter));
            Assert.AreEqual("过账状态筛选", AutomationProperties.GetName(statusFilter));
            var customerPopup = await Open(customerFilter); var customerList = (ListBox)customerFilter.FindName("PART_ListBox");
            Assert.HasCount(3, customerList.Items); Assert.HasCount(0, vm.Headers.Items);
            Toggle(customerList, 0); Toggle(customerList, 1); Cancel(customerFilter); await Idle();
            Assert.HasCount(0, vm.Search.SelectedCustomers);
            customerPopup = await Open(customerFilter);
            ((TextBox)customerFilter.FindName("PART_SearchBox")).Text = "中文"; await Idle();
            Assert.HasCount(2, customerList.Items); Toggle(customerList, 0); Toggle(customerList, 1);
            Click(customerPopup, "确定"); await Idle(); Assert.HasCount(2, vm.Search.SelectedCustomers);
            var statusPopup = await Open(statusFilter); var statusList = (ListBox)statusFilter.FindName("PART_ListBox");
            ((TextBox)statusFilter.FindName("PART_SearchBox")).Text = "过账"; await Idle();
            Assert.HasCount(2, statusList.Items);
            var labels = Descendants(statusPopup.Child).OfType<TextBlock>().Select(x => x.Text).ToArray();
            CollectionAssert.Contains(labels, "未过账"); CollectionAssert.Contains(labels, "已过账");
            Toggle(statusList, 0); Toggle(statusList, 1); Click(statusPopup, "确定"); await Idle();
            await vm.SearchCommand.ExecuteAsync(null);
            CollectionAssert.AreEqual(new[] { customers[0].Uid, customers[1].Uid }, requests.Last().CustomerIds);
            CollectionAssert.AreEqual(new[] { PackingListPostingStatusEnum.Unposted, PackingListPostingStatusEnum.Posted }, requests.Last().PostingStatuses);
            customerPopup = await Open(customerFilter);
            ((TextBox)customerFilter.FindName("PART_SearchBox")).Text = "中文甲"; await Idle();
            Assert.HasCount(1, customerList.Items); Click(customerPopup, "清除"); Cancel(customerFilter); await Idle();
            Assert.HasCount(2, vm.Search.SelectedCustomers);
            customerPopup = await Open(customerFilter);
            ((TextBox)customerFilter.FindName("PART_SearchBox")).Text = "中文甲"; await Idle();
            Click(customerPopup, "清除"); Click(customerPopup, "确定"); await Idle();
            Assert.HasCount(0, vm.Search.SelectedCustomers);
            statusPopup = await Open(statusFilter); Toggle(statusList, 2); Cancel(statusFilter); await Idle();
            Assert.HasCount(2, vm.Search.PostingStatuses);
            statusPopup = await Open(statusFilter); Click(statusPopup, "清除"); Click(statusPopup, "确定"); await Idle();
            await vm.SearchCommand.ExecuteAsync(null);
            Assert.HasCount(0, requests.Last().CustomerIds); Assert.HasCount(0, requests.Last().PostingStatuses);
            vm.Search.SelectedCustomers.Add(new CustomerDto { Uid = customers[0].Uid, CustomerCode = "不同实例" });
            customerPopup = await Open(customerFilter);
            ((TextBox)customerFilter.FindName("PART_SearchBox")).Text = "C-SAME"; await Idle();
            Assert.HasCount(2, customerList.Items);
            Assert.IsTrue(ItemToggle(customerList, 0).IsChecked); Assert.IsFalse(ItemToggle(customerList, 1).IsChecked);
            Cancel(customerFilter);
        }
        finally { window.Close(); await Idle(); }
    });

    /// <summary>提供代码相同但身份不同的客户，证明筛选不会误按显示文字合并。</summary>
    private static CustomerDto[] Customers() => [new() { Uid = Guid.NewGuid(), CustomerCode = "C-SAME", CustomerName = "中文甲" },
        new() { Uid = Guid.NewGuid(), CustomerCode = "C-SAME", CustomerName = "中文乙" },
        new() { Uid = Guid.NewGuid(), CustomerCode = "EN-03", CustomerName = "English" }];

    /// <summary>离线 Manager 使用完整客户缓存，空服务端结果仍保留全部候选。</summary>
    private static PackingListManagerViewModel Manager(PackingListApiStub api, CustomerDto[] customers)
    {
        var ui = new PackingListUiStub();
        return new(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), new PackingListNavigationStub(), ui, ui,
            new ReferenceDataCacheServiceStub(customers: customers));
    }

    /// <summary>以空分页结果隔离筛选接线，不触发无关详情读取。</summary>
    private static ServiceResult<PagedResponseBase<PackingListManagerListItemDto>> EmptyPage() => ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>.Ok(new() { Items = [], TotalCount = 0 });

    /// <summary>打开真实触发按钮绑定的 Popup，等待列表容器生成。</summary>
    private static async Task<Popup> Open(MultiSelectLookupButton filter)
    {
        ((ToggleButton)filter.FindName("PART_Button")).SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        await Idle(); var popup = (Popup)filter.FindName("PART_Popup"); Assert.IsTrue(popup.IsOpen); return popup;
    }

    /// <summary>走实际键盘冒泡取消草稿，覆盖 Esc 关闭及焦点返回。</summary>
    private static void Cancel(MultiSelectLookupButton filter)
    {
        var search = (TextBox)filter.FindName("PART_SearchBox");
        search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.IsFalse(((Popup)filter.FindName("PART_Popup")).IsOpen);
    }

    /// <summary>通过辅助功能切换真实候选按钮，保留控件既有双向绑定。</summary>
    private static void Toggle(ListBox list, int index) => ((IToggleProvider)new ToggleButtonAutomationPeer(ItemToggle(list, index)).GetPattern(PatternInterface.Toggle)).Toggle();

    /// <summary>仅从实际生成的候选容器取得交互按钮。</summary>
    private static ToggleButton ItemToggle(ListBox list, int index)
    {
        list.UpdateLayout(); var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
        return Descendants(item).OfType<ToggleButton>().Single();
    }

    /// <summary>调用 Popup 内真实按钮的 Click 路由，避免绕过选择草稿确认。</summary>
    private static void Click(Popup popup, string content) => Descendants(popup.Child).OfType<Button>().Single(x => Equals(x.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>枚举当前可视子树，不缓存任何虚拟化容器。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>等待绑定和布局处理结束，避免使用固定延时。</summary>
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
}
