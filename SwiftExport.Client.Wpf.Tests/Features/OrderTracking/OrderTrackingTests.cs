using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DataGridExtensions;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExportClient.Features.OrderTracking.MaterialDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.MaterialDetail.Views;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.DemandDetail.Views;
using SwiftExportClient.Features.OrderTracking.Overview.ViewModels;
using SwiftExportClient.Features.OrderTracking.Overview.Views;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Resources.Commands;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace SwiftExport.Client.Wpf.Tests.Features.OrderTracking;

/// <summary>验证来源主从、统一搜索、真实筛选弹窗以及独立跟踪区域。</summary>
[TestClass]
[DoNotParallelize]
public sealed partial class OrderTrackingTests
{
    /// <summary>主表仍分页筛来源，子表只按稳定身份一次读取超过200条的完整集合。</summary>
    [TestMethod]
    public void FiltersAndPaging_UseIndependentContract() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; await vm.InitializeAsync();
        CollectionAssert.AreEqual(new[] { "SO" }, f.Api.SourceRequests.Single().SourceTypes); Assert.AreEqual(0, f.Api.Requests.Count);
        vm.Keyword = " PO-2026 "; vm.SelectedCustomerCodes.Add("C1"); vm.RequiredDateFrom = new DateTime(2026, 10, 1); vm.SelectedLifecycleStatuses.Add(vm.LifecycleOptions[0]);
        f.Api.Search = (type, id, _) => Task.FromResult(Demands(type, id, Enumerable.Range(0, 301).Select(index => new OrderTrackingListItemDto
        { GrossDemandId = Guid.NewGuid(), SourceUid = id, Sku = "OTHER-" + index, CustomerCode = "C2", LifecycleStatus = index % 2 == 0 ? GrossDemandLifecycleStatusEnum.Closed : GrossDemandLifecycleStatusEnum.Active }).ToArray()));
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual("PO-2026", f.Api.SourceRequests.Last().Keyword); await ChooseSource(f);
        Assert.AreEqual(301, vm.Items.Count); Assert.AreEqual(301, vm.LoadedDemandCount); Assert.AreEqual(301, vm.VisibleDemandCount);
        Assert.IsTrue(vm.Items.Any(x => x.LifecycleStatus == GrossDemandLifecycleStatusEnum.Closed)); Assert.IsTrue(vm.Items.All(x => x.CustomerCode == "C2"));
        Assert.AreEqual(vm.SelectedSource!.SourceType, f.Api.Requests.Single().SourceType); Assert.AreEqual(vm.SelectedSource.SourceGroupId, f.Api.Requests.Single().SourceGroupId);
        StringAssert.Contains(vm.ResultSummary, "全来源已加载 301");
        await vm.SourcePageChangeCommand.ExecuteAsync(Change(2)); Assert.AreEqual(2, vm.SourcePageIndex); Assert.IsNull(vm.SelectedSource); Assert.AreEqual(0, vm.Items.Count);
        foreach (var type in new[] { "CR", "MD" }) { vm.SelectedSourceType = type; await vm.SearchCommand.ExecuteAsync(null); CollectionAssert.AreEqual(new[] { type }, f.Api.SourceRequests.Last().SourceTypes); }
        await vm.ClearFiltersCommand.ExecuteAsync(null); var source = f.Api.SourceRequests.Last();
        CollectionAssert.AreEqual(new[] { "SO" }, source.SourceTypes); Assert.AreEqual("", source.Keyword); Assert.AreEqual(0, source.CustomerCodes.Count); Assert.AreEqual(0, source.LifecycleStatuses.Count); Assert.IsNull(source.RequiredDateFrom); Assert.AreEqual(0, source.AttentionCodes.Count);
    });

    /// <summary>同来源全量刷新失败保留已接纳事实；来源查询失败和无效日期不接纳新页码。</summary>
    [TestMethod]
    public void FailedPageAndInvalidDates_PreserveAcceptedFacts() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; await vm.InitializeAsync(); await ChooseSource(f); vm.SelectedItem = vm.Items.First(); var row = vm.SelectedItem;
        f.Api.Search = (_, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceDemandsDto>.Fail("读取失败")); await vm.LoadSelectedSourceCommand.ExecuteAsync(null);
        Assert.AreEqual(2, vm.LoadedDemandCount); Assert.AreSame(row, vm.Items.First()); Assert.AreSame(row, vm.SelectedItem); Assert.AreEqual("读取失败", vm.DemandLoadError);
        f.Api.Search = (_, _, _) => Task.FromResult(Demands("CR", Guid.NewGuid(), Row("wrong"))); await vm.LoadSelectedSourceCommand.ExecuteAsync(null);
        Assert.AreSame(row, vm.Items.First()); StringAssert.Contains(vm.DemandLoadError!, "身份不一致");
        var source = vm.Sources.First(); f.Api.SourceSearch = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Fail("来源失败"));
        await vm.SourcePageChangeCommand.ExecuteAsync(Change(2)); Assert.AreEqual(1, vm.SourcePageIndex); Assert.AreSame(source, vm.Sources.First()); Assert.IsNull(vm.SelectedSource); Assert.AreEqual(0, vm.Items.Count);
        var count = f.Api.SourceRequests.Count; vm.RequiredDateFrom = new DateTime(2026, 11, 1); vm.RequiredDateTo = new DateTime(2026, 10, 1); await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(count, f.Api.SourceRequests.Count); Assert.IsFalse(vm.IsLoadingSources); StringAssert.Contains(vm.ValidationSummary!, "开始日期");
    });

    /// <summary>来源查询冻结多选集合，较新请求取消并拦截旧响应。</summary>
    [TestMethod]
    public void LatestQueryWins_AndFreezesSelections() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; var old = PendingSources(); var latest = PendingSources();
        f.Api.SourceSearch = (_, _) => f.Api.SourceRequests.Count == 1 ? old.Task : latest.Task;
        vm.SelectedCustomerCodes.Add("C1"); var first = vm.SearchCommand.ExecuteAsync(null);
        vm.SelectedCustomerCodes.Clear(); vm.SelectedCustomerCodes.Add("C2"); var second = vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Api.SourceTokens[0].IsCancellationRequested); CollectionAssert.AreEqual(new[] { "C1" }, f.Api.SourceRequests[0].CustomerCodes);
        old.SetResult(SourcePage(f.Api.Groups[0])); await first; Assert.IsTrue(vm.IsLoadingSources); Assert.AreEqual(0, vm.Sources.Count);
        latest.SetResult(SourcePage(f.Api.Groups[1])); await second; Assert.AreEqual(f.Api.Groups[1].SourceGroupId, vm.Sources.Single().SourceGroupId); Assert.IsFalse(vm.IsLoadingSources);
    });

    /// <summary>关键词编辑立即清除来源选择和两表页码，迟到响应不能恢复旧上下文。</summary>
    [TestMethod]
    public void KeywordChange_InvalidatesBothPendingLevels() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; await vm.InitializeAsync(); await ChooseSource(f);
        var pendingChild = PendingPage(); f.Api.Search = (_, _, _) => pendingChild.Task; var childLoad = vm.LoadSelectedSourceCommand.ExecuteAsync(null); vm.Keyword = "new";
        Assert.IsTrue(f.Api.Tokens.Last().IsCancellationRequested); Assert.IsNull(vm.SelectedSource); Assert.AreEqual(1, vm.SourcePageIndex); Assert.AreEqual(0, vm.LoadedDemandCount); Assert.AreEqual(0, vm.Items.Count);
        pendingChild.SetResult(Page(Row("late"))); await childLoad; Assert.AreEqual(0, vm.Items.Count);
        var pendingSource = PendingSources(); f.Api.SourceSearch = (_, _) => pendingSource.Task; var sourceLoad = vm.SearchCommand.ExecuteAsync(null); vm.Keyword = "newer";
        pendingSource.SetResult(SourcePage(f.Api.Groups[0])); await sourceLoad; Assert.AreEqual(0, vm.Sources.Count); Assert.IsFalse(vm.IsLoadingSources);
    });

    /// <summary>同号不同来源组分别定位；缺失SourceUid时仍使用SourceGroupId，切换后拒绝旧子表。</summary>
    [TestMethod]
    public void SourceSwitch_UsesGroupIdentityAndRejectsOldChildren() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); f.Api.Groups[1].SourceNo = f.Api.Groups[0].SourceNo; var vm = f.Overview; await vm.InitializeAsync();
        var old = PendingPage(); var latest = PendingPage(); f.Api.Search = (_, _, _) => f.Api.Requests.Count == 1 ? old.Task : latest.Task;
        vm.SelectedSource = vm.Sources[0]; var first = vm.LoadSelectedSourceCommand.ExecuteAsync(null); vm.SelectedSource = vm.Sources[1]; var second = vm.LoadSelectedSourceCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Api.Tokens[0].IsCancellationRequested); Assert.AreNotEqual(f.Api.Requests[0].SourceGroupId, f.Api.Requests[1].SourceGroupId);
        Assert.AreEqual(Guid.Empty, vm.SelectedSource.SourceUid); Assert.AreEqual(vm.SelectedSource.SourceGroupId, f.Api.Requests[1].SourceGroupId);
        old.SetResult(Page(Row("old"))); await first; Assert.IsTrue(vm.IsLoading); Assert.AreEqual(0, vm.Items.Count);
        latest.SetResult(Page(Row("new", vm.SelectedSource!.SourceGroupId))); await second; Assert.AreEqual("new", vm.Items.Single().Sku);
    });

    /// <summary>释放总览取消来源读取，不接受迟到成功。</summary>
    [TestMethod]
    public void DisposedOverview_RejectsLateSuccess() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var pending = PendingSources(); f.Api.SourceSearch = (_, _) => pending.Task;
        var load = f.Overview.SearchCommand.ExecuteAsync(null); f.Overview.Dispose(); Assert.IsTrue(f.Api.SourceTokens.Single().IsCancellationRequested);
        pending.SetResult(SourcePage(f.Api.Groups[0])); await load; Assert.AreEqual(0, f.Overview.Sources.Count);
    });

    /// <summary>会话撤权清除两表和候选，恢复权限后不会访问已释放令牌。</summary>
    [TestMethod]
    public void UserChange_ClearsFactsAndCancelsRequests() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Overview.InitializeAsync(); await ChooseSource(f);
        var pending = PendingPage(); f.Api.Search = (_, _, _) => pending.Task; var load = f.Overview.LoadSelectedSourceCommand.ExecuteAsync(null); f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [] });
        pending.SetResult(Page(Row("late"))); await load; Assert.AreEqual(0, f.Overview.Items.Count); Assert.AreEqual(0, f.Overview.Sources.Count); Assert.AreEqual(0, f.Overview.CustomerCodeOptions.Count);
        Assert.IsFalse(f.Overview.SearchCommand.CanExecute(null)); Assert.IsFalse(f.Overview.OpenSelectedCommand.CanExecute(null));
        f.User.SetCurrentUser(new MeDto { UserId = "v", Permissions = [PermissionKeys.OrderTrackingView] }); await f.Overview.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(2, f.Overview.Sources.Count);
    });

    /// <summary>客户候选取消与来源请求相互独立。</summary>
    [TestMethod]
    public void CustomerCandidates_RejectStaleRefresh() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var old = new TaskCompletionSource<ServiceResult<List<string>>>(); f.Api.Customers = _ => old.Task; var first = f.Overview.RefreshCommand.ExecuteAsync(null);
        f.Api.Customers = _ => Task.FromResult(ServiceResult<List<string>>.Ok(["C2"])); await f.Overview.RefreshCommand.ExecuteAsync(null); old.SetResult(ServiceResult<List<string>>.Ok(["C1"])); await first;
        CollectionAssert.AreEqual(new[] { "C2" }, f.Overview.CustomerCodeOptions.ToArray()); Assert.IsTrue(f.Api.CustomerTokens[0].IsCancellationRequested);
    });
    /// <summary>基础明细直接保留服务器快照和当前 PO，拒绝错误需求身份。</summary>
    [TestMethod]
    public void Detail_UsesStableDemandIdentityAndReadonlyFacts() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); using var vm = f.NewDetail(); var id = Guid.NewGuid();
        var header = new DemandTrackingHeaderDto { GrossDemandId = id, SourceUid = Guid.NewGuid(), SourceNo = "SO1", Sku = "snapshot", CustomerPo = "current PO", RequiredDate = null, Unit = null!, DemandQty = 9 };
        f.Api.Header = (_, _) => Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Ok(header));
        await vm.InitializeDocumentAsync(id, CancellationToken.None);
        Assert.AreSame(header, vm.Detail); Assert.IsTrue(vm.IsReadOnly); Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.AreEqual(id, f.Api.HeaderIds.Single());
        f.Api.Header = (_, _) => Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = Guid.NewGuid() }));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(header, vm.Detail); StringAssert.Contains(vm.ValidationSummary!, "身份不一致");
        f.Api.Header = (_, _) => Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Fail("明细失败"));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(header, vm.Detail); Assert.AreEqual("明细失败", vm.ValidationSummary);
    });

    /// <summary>明细切换初始化与释放均取消旧读取，响应必须匹配最新文档身份。</summary>
    [TestMethod]
    public void Detail_RejectsStaleAndDisposedResponses() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); using var vm = f.NewDetail();
        var old = new TaskCompletionSource<ServiceResult<DemandTrackingHeaderDto>>();
        var id1 = Guid.NewGuid(); var id2 = Guid.NewGuid();
        f.Api.Header = (_, _) => old.Task; var first = vm.InitializeDocumentAsync(id1, CancellationToken.None);
        f.Api.Header = (id, _) => Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = id }));
        await vm.InitializeDocumentAsync(id2, CancellationToken.None);
        Assert.IsTrue(f.Api.HeaderTokens[0].IsCancellationRequested);
        old.SetResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = id1 })); await first;
        Assert.AreEqual(id2, vm.Detail!.GrossDemandId);
        var late = new TaskCompletionSource<ServiceResult<DemandTrackingHeaderDto>>();
        f.Api.Header = (_, _) => late.Task; var refresh = vm.RefreshCommand.ExecuteAsync(null);
        var accepted = vm.Detail; vm.Dispose();
        Assert.IsTrue(f.Api.HeaderTokens.Last().IsCancellationRequested);
        late.SetResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = id2 })); await refresh;
        Assert.AreSame(accepted, vm.Detail);
    });

    /// <summary>外部取消和会话撤权均阻止明细响应接纳，撤权后不能继续调用基础接口。</summary>
    [TestMethod]
    public void Detail_RespectsExternalCancellationAndRevokedPermission() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); using var vm = f.NewDetail(); var id = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<ServiceResult<DemandTrackingHeaderDto>>();
        f.Api.Header = (_, _) => pending.Task;
        var load = vm.InitializeDocumentAsync(id, cancellation.Token); cancellation.Cancel();
        Assert.IsTrue(f.Api.HeaderTokens.Single().IsCancellationRequested);
        pending.SetResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = id })); await load;
        Assert.IsNull(vm.Detail);
        f.Api.Header = (key, _) => Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = key }));
        await vm.RefreshCommand.ExecuteAsync(null); Assert.IsNotNull(vm.Detail);
        pending = new(); f.Api.Header = (_, _) => pending.Task;
        var refresh = vm.RefreshCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [] });
        pending.SetResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = id })); await refresh;
        Assert.IsNull(vm.Detail); Assert.IsFalse(vm.IsLoading); Assert.IsFalse(vm.RefreshCommand.CanExecute(null));
        var count = f.Api.HeaderIds.Count;
        await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(count, f.Api.HeaderIds.Count);
    });


    /// <summary>同需求复用明细页签，返回保留关键词、来源页、需求页和选择。</summary>
    [TestMethod]
    public void Navigation_ReusesDemandTabsAndPreservesOverview() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Navigation.OpenAsync(OrderTrackingOverviewViewModel.RouteKey); var tab = f.Navigation.SelectedTab!; var vm = (OrderTrackingOverviewViewModel)tab.ContentViewModel!;
        vm.Keyword = "SO"; await vm.SearchCommand.ExecuteAsync(null); await vm.SourcePageChangeCommand.ExecuteAsync(Change(2));
        vm.SelectedSource = vm.Sources[0]; await vm.LoadSelectedSourceCommand.ExecuteAsync(null); vm.SelectedItem = vm.Items.First(); var selected = vm.SelectedItem;
        await vm.OpenSelectedCommand.ExecuteAsync(null); var detailTab = f.Navigation.SelectedTab; await vm.OpenSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(detailTab, f.Navigation.SelectedTab); Assert.AreEqual(2, f.Navigation.OpenTabs.Count); Assert.AreEqual(selected.GrossDemandId.ToString("N"), detailTab!.DocumentKey);
        await ((DemandTrackingDetailViewModel)detailTab.ContentViewModel!).ReturnToOverviewCommand.ExecuteAsync(null);
        Assert.AreSame(tab, f.Navigation.SelectedTab); Assert.AreEqual("SO", vm.Keyword); Assert.AreEqual(2, vm.SourcePageIndex); Assert.AreEqual(2, vm.LoadedDemandCount); Assert.AreSame(selected, vm.SelectedItem);
        vm.SelectedItem = vm.Items.Last(); await vm.OpenSelectedCommand.ExecuteAsync(null); Assert.AreEqual(3, f.Navigation.OpenTabs.Count);
    });

    /// <summary>跟踪权限独立于销售和旧需求权限，隐藏明细没有额外菜单入口。</summary>
    [TestMethod]
    public void Navigation_UsesDedicatedPermissionAndHiddenDetail() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var registry = new NavigationRouteRegistry(); var access = new NavigationAccessService(registry, f.User);
        Assert.IsTrue(access.CanAccess(OrderTrackingOverviewViewModel.RouteKey)); Assert.IsTrue(access.CanAccess(DemandTrackingDetailViewModel.RouteKey)); Assert.IsFalse(access.CanAccess("sales-orders")); Assert.IsFalse(access.CanAccess("customers"));
        var menu = registry.MenuItems.Single(x => x.RouteKey == NavigationModuleKeys.OrderTracking); Assert.AreEqual(1, menu.Children.Count); Assert.AreEqual(OrderTrackingOverviewViewModel.RouteKey, menu.Children[0].RouteKey);
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.SalesOrderView, PermissionKeys.DemandPlanningSourceView] });
        await f.Navigation.OpenAsync(OrderTrackingOverviewViewModel.RouteKey); await f.Navigation.OpenDocumentAsync(DemandTrackingDetailViewModel.RouteKey, "denied", Guid.NewGuid());
        Assert.AreEqual(0, f.Navigation.OpenTabs.Count); await f.Overview.InitializeAsync(); Assert.AreEqual(0, f.Api.SourceRequests.Count);
    });

    /// <summary>真实客户多选弹窗确定两个代码后才提交，清空和取消不残留临时勾选。</summary>
    [TestMethod]
    public void CustomerPopup_AppliesTwoSelectionsAndClears() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Overview.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = f.Overview };
        await WithWindow(view, async () =>
        {
            var selector = (MultiSelectLookupButton)view.FindName("CustomerCodesSelector"); OpenPopup(selector); await Idle();
            var popup = (Popup)selector.FindName("PART_Popup"); var choices = Descendants(popup.Child).OfType<ToggleButton>().ToArray(); Assert.AreEqual(2, choices.Length);
            foreach (var choice in choices) Toggle(choice); Assert.AreEqual(0, f.Overview.SelectedCustomerCodes.Count);
            await Click(Descendants(popup.Child).OfType<Button>().Single(x => Equals(x.Content, "确定"))); await f.Overview.SearchCommand.ExecuteAsync(null);
            CollectionAssert.AreEquivalent(new[] { "C1", "C2" }, f.Api.SourceRequests.Last().CustomerCodes); Assert.AreSame(f.Overview.SelectedCustomerCodes, selector.SelectedItems);
            await f.Overview.ClearFiltersCommand.ExecuteAsync(null); Assert.AreEqual(0, selector.SelectedItems.Count); Assert.AreEqual(0, f.Api.SourceRequests.Last().CustomerCodes.Count);
            OpenPopup(selector); await Idle(); choices = Descendants(popup.Child).OfType<ToggleButton>().ToArray(); Assert.IsTrue(choices.All(x => x.IsChecked == false));
            Toggle(choices[0]); popup.IsOpen = false; Assert.AreEqual(0, f.Overview.SelectedCustomerCodes.Count);
        });
    });

    /// <summary>真实来源漏斗隐藏已选来源时清除子表，候选及筛选仅作用当前加载页。</summary>
    [TestMethod]
    public void HeaderPopup_FiltersLoadedPagesAndClearsHiddenSource() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Overview.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = f.Overview };
        await WithWindow(view, async () =>
        {
            var sources = (DataGrid)view.FindName("SourcesGrid"); var demands = (DataGrid)view.FindName("TrackingGrid"); sources.SelectedItem = f.Overview.Sources[0]; await Settle(); Assert.AreEqual(2, demands.Items.Count);
            var (filter, popup) = await OpenFilter(sources, "来源单号"); var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter);
            Assert.AreEqual(2, choices.Items.Count); Assert.AreEqual(41, f.Overview.SourceTotalCount);
            choices.UnselectAll(); choices.SelectedItems.Add(f.Api.Groups[1].SourceNo); await Settle(); await Settle();
            Assert.AreEqual(1, sources.Items.Count); Assert.AreEqual(2, f.Overview.Sources.Count); Assert.IsNull(f.Overview.SelectedSource); Assert.AreEqual(0, f.Overview.Items.Count); Assert.IsFalse(f.Overview.OpenSelectedCommand.CanExecute(null));
            await Click((Button)filter.Template.FindName("ClearAllFilters", filter)); await Settle(); Assert.AreEqual(2, sources.Items.Count);
            filter.IsPopupOpen = false; sources.SelectedItem = f.Overview.Sources[0]; await Settle();
            var (childFilter, _) = await OpenFilter(demands, "产品编码"); var childChoices = (ListBox)childFilter.Template.FindName("PART_ListBox", childFilter);
            childChoices.UnselectAll(); childChoices.SelectedItems.Add("P-1002"); await Settle(); await Settle();
            Assert.AreEqual(1, demands.Items.Count); Assert.AreEqual(2, f.Overview.Items.Count); StringAssert.Contains(f.Overview.ResultSummary, "当前筛选显示 1"); Assert.AreEqual(2, f.Overview.LoadedDemandCount); Assert.AreEqual(1, f.Overview.VisibleDemandCount);
            childFilter.IsPopupOpen = false; DataGridFilterCommands.Clear.Execute(demands); await Settle(); Assert.AreEqual(2, demands.Items.Count);
        });
    });

    /// <summary>实际布局固定整行，原生复制、列显隐、行双击与统一搜索回车都可用。</summary>
    [TestMethod]
    public void Views_AreReadonlyAndUseExistingGridMenus() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Overview.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = f.Overview };
        using var errors = new StringWriter(); using var listener = new TextWriterTraceListener(errors); var trace = PresentationTraceSources.DataBindingSource; var previous = trace.Switch.Level; trace.Switch.Level = SourceLevels.Error; trace.Listeners.Add(listener);
        try
        {
            await WithWindow(view, async () =>
            {
                var sources = (DataGrid)view.FindName("SourcesGrid"); var grid = (DataGrid)view.FindName("TrackingGrid"); sources.SelectedItem = f.Overview.Sources[0]; await Settle();
                foreach (var current in new[] { sources, grid })
                {
                    Assert.IsTrue(current.IsReadOnly); Assert.AreEqual(DataGridSelectionUnit.FullRow, current.SelectionUnit); Assert.IsFalse(current.CanUserAddRows);
                    Assert.AreEqual(DataGridClipboardCopyMode.IncludeHeader, current.ClipboardCopyMode); Assert.IsTrue(current.GetIsAutoFilterEnabled());
                    Assert.AreEqual("查看", ((MenuItem)current.ContextMenu.Items[0]).Header); Assert.AreEqual(1, current.ContextMenu.Items.OfType<MenuItem>().Count(x => Equals(x.Header, "查看")));
                    Assert.IsTrue(current.ContextMenu.Items.OfType<MenuItem>().Any(x => x.Command == ApplicationCommands.Copy));
                }
                Assert.IsFalse(Descendants(view).OfType<CheckBox>().Any(x => Equals(x.Content, "格子模式"))); Assert.AreEqual(1, Descendants(view).OfType<PageNavigator>().Count(), "仅来源主表保留分页器。");
                CollectionAssert.AreEqual(new[] { "图片", "客户", "来源单号", "产品编码", "客户型号", "中性型号", "需求数量", "单位", "要求交期", "生命周期", "关注事项", "摘要已交", "摘要未交", "逾期", "物料条数", "采购承接", "关联采购整行" }, grid.Columns.Where(x => x.Visibility == Visibility.Visible).Select(x => (string)x.Header).ToArray());
                Assert.IsTrue(grid.Columns.Where(x => x.Visibility == Visibility.Visible).Take(10).Sum(x => x.ActualWidth) <= 1200); SavePreview(view, "overview.png");
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)!; var cell = Descendants(row).OfType<DataGridCell>().First(); var header = Descendants(grid).OfType<DataGridColumnHeader>().First();
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = header }); Assert.AreEqual(0, f.Navigation.OpenTabs.Count);
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = cell }); Assert.AreSame(grid.Items[0], f.Overview.SelectedItem);
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = cell }); await Idle(); Assert.AreEqual(1, f.Navigation.OpenTabs.Count);
                var color = grid.Columns.Single(x => Equals(x.Header, "颜色")); Assert.AreEqual(Visibility.Collapsed, color.Visibility);
                grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true; await Idle(); var columnMenu = (MenuItem)grid.ContextMenu.Items[0]; columnMenu.IsSubmenuOpen = true; await Idle();
                var toggle = (MenuItem)columnMenu.ItemContainerGenerator.ContainerFromItem(color)!; Assert.IsNotNull(toggle); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, toggle)); Assert.AreEqual(Visibility.Visible, color.Visibility); grid.ContextMenu.IsOpen = false;
                var search = (TextBox)view.FindName("KeywordSearchBox"); search.Text = "INV"; Assert.IsTrue(search.Focus()); var count = f.Api.SourceRequests.Count;
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent }); await Idle();
                Assert.AreEqual(count + 1, f.Api.SourceRequests.Count); Assert.AreEqual("INV", f.Api.SourceRequests.Last().Keyword); Assert.IsNull(f.Overview.SelectedSource); Assert.AreEqual(0, f.Overview.Items.Count);
            });
            Assert.AreEqual("", errors.ToString(), "页面不能产生数据绑定错误。");
        }
        finally { trace.Listeners.Remove(listener); trace.Switch.Level = previous; }
    });
    /// <summary>首次激活前不读取；失败、无权、空及待核实不遮蔽其他成功区域。</summary>
    [TestMethod]
    public void Sections_LoadIndependentlyAndKeepDistinctStates() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewDetail(); var id = Guid.NewGuid(); await vm.InitializeDocumentAsync(id, CancellationToken.None); var header = vm.Detail; Assert.AreEqual(0, f.Api.SliceCalls.Count);
        f.Api.Purchase = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingPurchaseDto>>.Fail("采购暂不可用"));
        f.Api.Receipt = (_, _) => Task.FromResult(ServiceResult<DemandTrackingReceiptSectionDto>.Fail(ServiceErrorCodes.Forbidden, "收货读取无权"));
        f.Api.Delivery = (key, _) => Task.FromResult(ServiceResult<DemandTrackingDeliverySectionDto>.Ok(new() { GrossDemandId = key, DeliveredQty = null, RemainingQty = null, ReviewReason = "历史证据待核实" }));
        await Task.WhenAll(vm.Materials.ReloadCommand.ExecuteAsync(null), vm.Purchases.ReloadCommand.ExecuteAsync(null), vm.Receipts.ReloadCommand.ExecuteAsync(null), vm.Deliveries.ReloadCommand.ExecuteAsync(null));
        Assert.AreEqual("已加载", vm.Materials.StatusText); var materials = vm.Materials.Data;
        Assert.AreEqual("加载失败，可重试", vm.Purchases.StatusText); Assert.AreEqual("采购暂不可用", vm.Purchases.ErrorMessage); Assert.AreEqual("无权查看", vm.Receipts.StatusText);
        Assert.AreEqual("待核实", vm.Deliveries.StatusText); Assert.IsNull(vm.Deliveries.Data!.DeliveredQty); Assert.AreSame(header, vm.Detail); Assert.AreSame(materials, vm.Materials.Data);
        f.Api.Purchase = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingPurchaseDto>>.Ok([new() { HasPurchaseTarget = false, PurchaseLineQty = null }]));
        await vm.Purchases.ReloadCommand.ExecuteAsync(null); Assert.AreEqual("待核实", vm.Purchases.StatusText); Assert.AreSame(materials, vm.Materials.Data);
        f.Api.Material = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingMaterialDto>>.Ok([])); await vm.Materials.ReloadCommand.ExecuteAsync(null); Assert.AreEqual("暂无记录", vm.Materials.StatusText); Assert.IsNull(vm.Materials.ErrorMessage);
        f.Api.Receipt = (_, _) => Task.FromResult(ServiceResult<DemandTrackingReceiptSectionDto>.Ok(new() { GrossDemandId = Guid.NewGuid() })); await vm.Receipts.ReloadCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.Receipts.ErrorMessage!, "身份不一致"); Assert.AreSame(header, vm.Detail);
    });

    /// <summary>缺任一必要权限不发区域请求，收货必须同时具有采购读取权限。</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("Inventory.Inbound.View")]
    [DataRow("Procurement.Order.Read")]
    public void Sections_CheckPermissionsBeforeRequests(string additionalPermission) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = string.IsNullOrEmpty(additionalPermission) ? [PermissionKeys.OrderTrackingView] : [PermissionKeys.OrderTrackingView, additionalPermission] });
        using var vm = f.NewDetail(); await vm.InitializeDocumentAsync(Guid.NewGuid(), CancellationToken.None);
        await Task.WhenAll(vm.Materials.EnsureLoadedAsync(), vm.Purchases.EnsureLoadedAsync(), vm.Receipts.EnsureLoadedAsync(), vm.Deliveries.EnsureLoadedAsync());
        Assert.AreEqual(0, f.Api.SliceCalls.Count(x => x.Area == "receipts")); Assert.AreEqual("无权查看", vm.Receipts.StatusText); Assert.AreEqual("无权查看", vm.Materials.StatusText); Assert.AreEqual("无权查看", vm.Deliveries.StatusText);
        Assert.AreEqual(additionalPermission == PermissionKeys.ProcurementOrderRead ? 1 : 0, f.Api.SliceCalls.Count(x => x.Area == "purchases")); Assert.IsNotNull(vm.Detail);
    });

    /// <summary>切需求取消区域旧读取，切区域不相互取消，撤权清除全部区域。</summary>
    [TestMethod]
    public void Sections_RejectStaleDemandAndSessionResponses() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewDetail(); var id1 = Guid.NewGuid(); var id2 = Guid.NewGuid(); await vm.InitializeDocumentAsync(id1, CancellationToken.None);
        var old = new TaskCompletionSource<ServiceResult<List<DemandTrackingMaterialDto>>>(); f.Api.Material = (_, _) => old.Task;
        vm.SelectedSectionIndex = 1; var first = vm.LoadSelectedSectionCommand.ExecuteAsync(null); vm.SelectedSectionIndex = 2; await vm.LoadSelectedSectionCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Api.SliceCalls.First(x => x.Area == "materials").Token.IsCancellationRequested, "切区域不能取消另一区域。");
        vm.SelectedSectionIndex = 0; await vm.InitializeDocumentAsync(id2, CancellationToken.None); Assert.IsTrue(f.Api.SliceCalls.First(x => x.Area == "materials").Token.IsCancellationRequested);
        f.Api.Material = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingMaterialDto>>.Ok([new() { Sku = "new" }])); await vm.Materials.ReloadCommand.ExecuteAsync(null);
        old.SetResult(ServiceResult<List<DemandTrackingMaterialDto>>.Ok([new() { Sku = "old" }])); await first; Assert.AreEqual("new", vm.Materials.Data!.Single().Sku);
        var pending = new TaskCompletionSource<ServiceResult<DemandTrackingDeliverySectionDto>>(); f.Api.Delivery = (_, _) => pending.Task;
        var load = vm.Deliveries.ReloadCommand.ExecuteAsync(null); f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [] });
        Assert.IsTrue(f.Api.SliceCalls.Last().Token.IsCancellationRequested); pending.SetResult(ServiceResult<DemandTrackingDeliverySectionDto>.Ok(new() { GrossDemandId = id2, DeliveredQty = 99 })); await load;
        Assert.IsNull(vm.Materials.Data); Assert.IsNull(vm.Purchases.Data); Assert.IsNull(vm.Deliveries.Data); Assert.IsNull(vm.Detail);
    });

    /// <summary>关闭页签取消区域读取，迟到错误不能覆盖释放后的状态。</summary>
    [TestMethod]
    public void Sections_DisposeCancelsPendingReads() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewDetail(); await vm.InitializeDocumentAsync(Guid.NewGuid(), CancellationToken.None);
        var pending = new TaskCompletionSource<ServiceResult<DemandTrackingReceiptSectionDto>>(); f.Api.Receipt = (_, _) => pending.Task;
        var load = vm.Receipts.ReloadCommand.ExecuteAsync(null); vm.Dispose(); Assert.IsTrue(f.Api.SliceCalls.Last().Token.IsCancellationRequested);
        pending.SetResult(ServiceResult<DemandTrackingReceiptSectionDto>.Fail("late")); await load; Assert.IsNull(vm.Receipts.Data); Assert.IsNull(vm.Receipts.ErrorMessage);
    });

    /// <summary>目标缺失不显示默认草稿，反向交付与正式交付使用不同中文状态。</summary>
    [TestMethod]
    public void EvidenceText_DoesNotInventMissingPurchaseStatus()
    {
        var converter = new TrackingEvidenceTextConverter();
        Assert.AreEqual("目标缺失 / 待核实", converter.Convert(new DemandTrackingPurchaseDto { HasPurchaseTarget = false }, typeof(string), "PurchaseConfirmation", CultureInfo.InvariantCulture));
        Assert.AreEqual("目标缺失 / 待核实", converter.Convert(new DemandTrackingPurchaseDto { HasPurchaseTarget = false }, typeof(string), "PurchaseLifecycle", CultureInfo.InvariantCulture));
        Assert.AreEqual("待核实", converter.Convert(null!, typeof(string), "Receipt", CultureInfo.InvariantCulture));
        Assert.AreEqual("待核实", converter.Convert(new DemandTrackingReceiptDto { CountsTowardReceipt = null }, typeof(string), "ReceiptPosting", CultureInfo.InvariantCulture));
        Assert.AreEqual("待核实", converter.Convert(new DemandTrackingReceiptDto { ReviewReason = "收货头缺失", CountsTowardReceipt = false }, typeof(string), "ReceiptPosting", CultureInfo.InvariantCulture));
        Assert.AreEqual("反向历史（不计入）", converter.Convert(new DemandTrackingDeliveryDto { ReversedDate = DateTime.Today }, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.AreEqual("正式交付（有效已过账）", converter.Convert(new DemandTrackingDeliveryDto { CountsTowardDelivery = true }, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    /// <summary>真实页签懒加载四区，全部只读整行并展示未知、历史及数量口径。</summary>
    [TestMethod]
    public void SectionTabs_ShowChineseStatesAndIndependentRows() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); using var vm = f.NewDetail(); await vm.InitializeDocumentAsync(Guid.NewGuid(), CancellationToken.None); var view = new DemandTrackingDetailView { DataContext = vm };
        using var output = new StringWriter(); using var listener = new TextWriterTraceListener(output); var trace = PresentationTraceSources.DataBindingSource; var previous = trace.Switch.Level; trace.Switch.Level = SourceLevels.Error; trace.Listeners.Add(listener);
        try
        {
            await WithWindow(view, async () =>
            {
                SavePreview(view, "detail.png"); Assert.AreEqual(0, f.Api.SliceCalls.Count);
                for (var index = 1; index <= 4; index++)
                {
                    vm.SelectedSectionIndex = index; await Settle(); var name = new[] { "Materials", "Purchases", "Receipts", "Deliveries" }[index - 1]; var grid = Descendants(view).OfType<DataGrid>().Single(x => x.Name == name + "Grid");
                    Assert.IsTrue(grid.IsReadOnly); Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit); Assert.IsTrue(grid.GetIsAutoFilterEnabled()); Assert.IsFalse(grid.CanUserAddRows);
                    Assert.IsTrue(grid.Items.Count > 0); Assert.AreEqual("查看", ((MenuItem)grid.ContextMenu.Items[0]).Header); SavePreview(view, name.ToLowerInvariant() + ".png");
                    var texts = Descendants(grid).OfType<TextBlock>().Select(x => x.Text).ToArray();
                    if (index == 2) { Assert.IsTrue(texts.Contains("目标缺失 / 待核实")); Assert.IsTrue(texts.Contains("待核实")); }
                    if (index == 3) { Assert.IsTrue(texts.Contains("待核实")); Assert.IsFalse(texts.Contains("未过账"), "缺失收货头不能显示默认未过账状态。"); }
                    if (index == 4) { Assert.IsTrue(texts.Contains("正式交付（有效已过账）")); Assert.IsTrue(texts.Contains("反向历史（不计入）")); }
                }
                Assert.AreEqual(4, f.Api.SliceCalls.Count); vm.SelectedSectionIndex = 1; await Idle(); Assert.AreEqual(4, f.Api.SliceCalls.Count);
            });
            Assert.AreEqual("", output.ToString(), "四区模板不能产生数据绑定错误。");
        }
        finally { trace.Listeners.Remove(listener); trace.Switch.Level = previous; }
    });

    private static PageChangeRequest Change(int page, int size = 20) => new() { OldIndex = 1, NewIndex = page, OldSize = 20, NewSize = size };
    private static async Task ChooseSource(Fixture fixture, int index = 0) { fixture.Overview.SelectedSource = fixture.Overview.Sources[index]; await fixture.Overview.LoadSelectedSourceCommand.ExecuteAsync(null); }
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static async Task Settle() { await Task.Delay(200); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    private static async Task WithWindow(FrameworkElement view, Func<Task> action)
    {
        var window = new Window { Content = view, Width = 1280, Height = 850, ShowInTaskbar = false };
        try { window.Show(); await Idle(); await action(); } finally { window.Close(); await Idle(); }
    }
    private static void OpenPopup(MultiSelectLookupButton selector) => Toggle((ToggleButton)selector.FindName("PART_Button"));
    private static void Toggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)!).Toggle();
    private static async Task Click(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); await Idle(); }
    private static async Task<(MultipleChoiceFilter Filter, Popup Popup)> OpenFilter(DataGrid grid, string header)
    {
        var column = grid.Columns.Single(x => Equals(x.Header, header)); grid.ScrollIntoView(grid.Items[0], column); grid.UpdateLayout(); await Idle();
        var filter = Descendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single(); Toggle((ToggleButton)filter.Template.FindName("FilterButton", filter)); await Settle();
        var popup = (Popup)filter.Template.FindName("Popup", filter); Assert.IsTrue(popup.IsOpen); return (filter, popup);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    /// <summary>导出实际WPF布局与替身数据，供检查首屏及数量提示。</summary>
    private static void SavePreview(FrameworkElement view, string name, [CallerFilePath] string source = "")
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "TestResults", "OrderTracking")); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) { var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight); context.DrawRectangle(Brushes.White, null, bounds); context.DrawRectangle(new VisualBrush(view), null, bounds); }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
    private static TaskCompletionSource<ServiceResult<OrderTrackingSourceDemandsDto>> PendingPage() => new();
    private static TaskCompletionSource<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> PendingSources() => new();
    /// <summary>摘要替身返回独立服务端结论，不在客户端测试中重算采购或交付。</summary>
    private static OrderTrackingSourceSummaryDto SourceSummaryRow(string type, Guid id) => new()
    {
        SourceType = type, SourceGroupId = id, ActiveDemandCount = 2,
        DeliveryAvailability = "Readable", MaterialAvailability = "Readable", PurchaseAvailability = "Readable", ReceiptAvailability = "Readable",
        DeliveryCompletedDemandCount = 1, DeliveryOutstandingDemandCount = 1, DeliveryReviewDemandCount = 0, OverdueDemandCount = 1, MaxOverdueDays = 3,
        MaterialEstablishedDemandCount = 2, NoActiveMaterialDemandCount = 0, MaterialReviewDemandCount = 0,
        PurchaseApplicableDemandCount = 2, PurchaseShortfallDemandCount = 1, PurchaseExcessDemandCount = 0, PurchaseReviewDemandCount = 0,
        ReceiptPurchaseLineCount = 1, ReceiptCompletedLineCount = 0, ReceiptOutstandingLineCount = 1, ReceiptExcessLineCount = 0, ReceiptReviewLineCount = 0, SharedPurchaseLineCount = 1,
        AttentionCodes = [OrderTrackingAttentionCodes.Overdue, OrderTrackingAttentionCodes.PurchaseShortfall, OrderTrackingAttentionCodes.ReceiptOutstanding]
    };
    /// <summary>模拟完整来源内每条需求的版本、单位与独立摘要身份。</summary>
    private static OrderTrackingDemandSummaryDto DemandSummaryRow(string type, Guid id, OrderTrackingListItemDto row) => new()
    {
        SourceType = type, SourceGroupId = id, GrossDemandId = row.GrossDemandId, DemandVersion = row.Version, Unit = row.Unit,
        IsActive = row.LifecycleStatus == GrossDemandLifecycleStatusEnum.Active,
        DeliveryAvailability = "Readable", MaterialAvailability = "Readable", PurchaseAvailability = "Readable", ReceiptAvailability = "Readable",
        DeliveryStatus = "Partial", DeliveredQty = 20, RemainingQty = 100, IsOverdue = true, OverdueDays = 3,
        ActiveMaterialCount = 1, HistoricalMaterialCount = 2, MaterialReviewCount = 0,
        PurchaseStatus = "Partial", ApplicableMaterialCount = 1, PurchaseShortfallMaterialCount = 1, PurchaseExcessMaterialCount = 0, PurchaseReviewMaterialCount = 0,
        ReceiptStatus = "Outstanding", ReceiptPurchaseLineCount = 1, ReceiptCompletedLineCount = 0, ReceiptOutstandingLineCount = 1, ReceiptExcessLineCount = 0, ReceiptReviewLineCount = 0, SharedPurchaseLineCount = 1,
        AttentionCodes = [OrderTrackingAttentionCodes.Overdue]
    };
    private static OrderTrackingListItemDto Row(string sku, Guid? source = null) => new() { GrossDemandId = Guid.NewGuid(), SourceUid = source ?? Guid.NewGuid(), SourceNo = "SO202610001", Sku = sku, CustomerSku = "C-" + sku, LinkSku = "LAMP-01", CustomerCode = "C1", DemandQty = 120, Unit = "PCS", RequiredDate = new DateTime(2026, 11, 15), SourceLineNumber = 1 };
    private static ServiceResult<OrderTrackingSourceDemandsDto> Page(params OrderTrackingListItemDto[] rows) => Demands("SO", rows.FirstOrDefault()?.SourceUid ?? Guid.Empty, rows);
    private static ServiceResult<OrderTrackingSourceDemandsDto> Demands(string type, Guid id, params OrderTrackingListItemDto[] rows) => ServiceResult<OrderTrackingSourceDemandsDto>.Ok(new() { SourceType = type, SourceGroupId = id, Items = rows.ToList() });
    private static ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>> SourcePage(params OrderTrackingSourceGroupDto[] groups) => ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Ok(new() { Items = groups.ToList(), TotalCount = 41, PageIndex = 1, PageSize = 20 });
    /// <summary>组合真实导航与API结果执行器，替身接口隔离运行数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        public readonly ApiStub Api = new(); public readonly CurrentUserService User = new();
        public Fixture(bool all = false)
        {
            User.SetCurrentUser(new MeDto { UserId = "u", Permissions = all ? [PermissionKeys.OrderTrackingView, PermissionKeys.DemandPlanningMaterialView, PermissionKeys.ProcurementOrderRead, PermissionKeys.InventoryInboundView, PermissionKeys.ShippingPackingListView] : [PermissionKeys.OrderTrackingView] });
            var services = new ServiceCollection(); services.AddSingleton<IOrderTrackingApi>(Api); services.AddSingleton<ICurrentUserService>(User);
            services.AddSingleton<IApiCallRunner>(new ApiCallRunner(NullLogger<ApiCallRunner>.Instance)); services.AddSingleton<INavigationRouteRegistry, NavigationRouteRegistry>(); services.AddSingleton<INavigationAccessService, NavigationAccessService>();
            services.AddSingleton<IStatusMessageService, StatusMessageService>(); services.AddSingleton<ILogger<NavigationService>>(NullLogger<NavigationService>.Instance); services.AddSingleton<INavigationTabFactory, NavigationTabFactory>(); services.AddSingleton<INavigationService, NavigationService>();
            services.AddTransient<OrderTrackingOverviewViewModel>(); services.AddTransient<DemandTrackingDetailViewModel>(); services.AddTransient<MaterialTrackingDetailViewModel>(); _provider = services.BuildServiceProvider(); Overview = _provider.GetRequiredService<OrderTrackingOverviewViewModel>();
        }
        public OrderTrackingOverviewViewModel Overview { get; }
        public INavigationService Navigation => _provider.GetRequiredService<INavigationService>();
        public DemandTrackingDetailViewModel NewDetail() => _provider.GetRequiredService<DemandTrackingDetailViewModel>();
        public MaterialTrackingDetailViewModel NewMaterialDetail() => _provider.GetRequiredService<MaterialTrackingDetailViewModel>();
        public void Dispose() => _provider.Dispose();
    }

    /// <summary>记录真实请求粒度和取消令牌，允许模拟忽略取消的响应。</summary>
    private sealed class ApiStub : IOrderTrackingApi
    {
        public readonly List<(string SourceType, Guid SourceGroupId)> Requests = []; public readonly List<CancellationToken> Tokens = [];
        public readonly List<OrderTrackingSourceSearchRequest> SourceRequests = []; public readonly List<CancellationToken> SourceTokens = [];
        public readonly List<CancellationToken> CustomerTokens = []; public readonly List<Guid> HeaderIds = []; public readonly List<CancellationToken> HeaderTokens = [];
        public readonly List<(string Area, Guid Id, CancellationToken Token)> SliceCalls = [];
        public readonly List<OrderTrackingSourceSummaryRequest> SummaryRequests = [];
        public readonly List<CancellationToken> SummaryTokens = [];
        public readonly List<(string Type, Guid Id, CancellationToken Token)> DemandSummaryRequests = [];
        private readonly Dictionary<(string Type, Guid Id), List<OrderTrackingListItemDto>> _summaryBases = [];
        public Func<OrderTrackingSourceSummaryRequest, CancellationToken, Task<ServiceResult<OrderTrackingSourceSummaryBatchDto>>> SourceSummary;
        public Func<string, Guid, CancellationToken, Task<ServiceResult<OrderTrackingSourceDemandSummariesDto>>> DemandSummary;
        public readonly OrderTrackingSourceGroupDto[] Groups = [new() { SourceGroupId = Guid.NewGuid(), SourceUid = Guid.NewGuid(), SourceType = "SO", SourceNo = "SO202610001", CustomerCode = "C1", CustomerPo = "PO-001", InvoiceNo = "INV-001", TotalDemandCount = 2, MatchedDemandCount = 2 }, new() { SourceGroupId = Guid.NewGuid(), SourceUid = Guid.Empty, SourceType = "SO", SourceNo = "SO202610002", CustomerCode = "C2", TotalDemandCount = 5, MatchedDemandCount = 1 }];
        public Func<string, Guid, CancellationToken, Task<ServiceResult<OrderTrackingSourceDemandsDto>>> Search = (type, id, _) => Task.FromResult(Demands(type, id, Row("P-1001", id), Row("P-1002", id)));
        public Func<OrderTrackingSourceSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>>> SourceSearch;
        public Func<CancellationToken, Task<ServiceResult<List<string>>>> Customers = _ => Task.FromResult(ServiceResult<List<string>>.Ok(["C1", "C2"]));
        public Func<Guid, CancellationToken, Task<ServiceResult<DemandTrackingHeaderDto>>> Header = (id, _) => Task.FromResult(ServiceResult<DemandTrackingHeaderDto>.Ok(new() { GrossDemandId = id, SourceType = "SO", SourceNo = "SO202610001", SourceLineNumber = 1, CustomerCode = "C1", CustomerName = "示例客户", Sku = "P-1001", CustomerSku = "C-P-1001", LinkSku = "LAMP-01", DemandQty = 120, Unit = "PCS", RequiredDate = new DateTime(2026, 11, 15) }));
        public Func<Guid, CancellationToken, Task<ServiceResult<List<DemandTrackingMaterialDto>>>> Material = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingMaterialDto>>.Ok([new() { NetDemandId = Guid.NewGuid(), DecompositionRevision = 1, LineNumber = 1, Sku = "M-1001", MaterialType = "配件", GrossQty = 240, NetQty = 240, Unit = "PCS", RequiredDate = new DateTime(2026, 11, 1) }]));
        public Func<Guid, CancellationToken, Task<ServiceResult<List<DemandTrackingPurchaseDto>>>> Purchase = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingPurchaseDto>>.Ok([new() { LinkId = Guid.NewGuid(), PurchaseOrderNo = "PO202610001", PurchaseLineNumber = 1, SupplierName = "示例供应商", Sku = "M-1001", Unit = "PCS", LinkedQty = 240, AllocatedQty = 240, HasPurchaseTarget = true, PurchaseLineQty = 1000 }, new() { LinkId = Guid.NewGuid(), PurchaseOrderNo = "历史采购", Sku = "M-OLD", Unit = "PCS", HasPurchaseTarget = false, PurchaseLineQty = null, ReviewReason = "采购目标缺失" }]));
        public Func<Guid, CancellationToken, Task<ServiceResult<DemandTrackingReceiptSectionDto>>> Receipt = (id, _) => Task.FromResult(ServiceResult<DemandTrackingReceiptSectionDto>.Ok(new() { GrossDemandId = id, Items = [new() { InboundDocumentNo = "IN202610001", LineNumber = 1, PurchaseOrderNo = "PO202610001", PurchaseLineNumber = 1, Sku = "M-1001", Qty = 500, Unit = "PCS", PostingStatus = InboundPostingStatusEnum.Posted, CountsTowardReceipt = true, ReceiptDate = new DateTime(2026, 10, 25), IsSharedPurchaseLine = true }, new() { InboundDocumentNo = "历史收货", LineNumber = 1, PurchaseOrderNo = "PO-OLD", Sku = "M-OLD", Qty = 30, Unit = "PCS", CountsTowardReceipt = null, ReviewReason = "收货头缺失" }] }));
        public Func<Guid, CancellationToken, Task<ServiceResult<DemandTrackingDeliverySectionDto>>> Delivery = (id, _) => Task.FromResult(ServiceResult<DemandTrackingDeliverySectionDto>.Ok(new() { GrossDemandId = id, DeliveredQty = 120, RemainingQty = 0, Unit = "PCS", BusinessDate = new DateTime(2026, 10, 19), IsOverdue = false, OverdueDays = 0, Items = [new() { PackingListNo = "PL202610001", LineNumber = 1, Qty = 120, Unit = "PCS", CountsTowardDelivery = true, PostedDate = new DateTime(2026, 11, 1) }, new() { PackingListNo = "PL-OLD", LineNumber = 1, Qty = 10, Unit = "PCS", PostedDate = new DateTime(2026, 10, 20), ReversedDate = new DateTime(2026, 10, 21) }] }));
        public Guid MaterialGrossDemandId = Guid.NewGuid();
        private static readonly Guid SharedPurchaseLineId = Guid.NewGuid();
        public Func<Guid, CancellationToken, Task<ServiceResult<MaterialTrackingDetailDto>>> MaterialHeader;
        public Func<Guid, CancellationToken, Task<ServiceResult<MaterialTrackingPurchaseSectionDto>>> MaterialPurchase;
        public Func<Guid, CancellationToken, Task<ServiceResult<DemandTrackingReceiptSectionDto>>> MaterialReceipt;
        public ApiStub()
        {
            SourceSearch = (_, _) => Task.FromResult(SourcePage(Groups));
            SourceSummary = (request, _) => Task.FromResult(ServiceResult<OrderTrackingSourceSummaryBatchDto>.Ok(new()
            {
                BusinessDate = new(2026, 10, 19), ReadAt = new(2026, 10, 19, 12, 0, 0, TimeSpan.FromHours(8)),
                Items = request.Sources.Select(key => SourceSummaryRow(key.SourceType, key.SourceGroupId)).ToList()
            }));
            DemandSummary = (type, id, _) => Task.FromResult(ServiceResult<OrderTrackingSourceDemandSummariesDto>.Ok(new()
            {
                SourceType = type, SourceGroupId = id, BusinessDate = new(2026, 10, 19), ReadAt = new(2026, 10, 19, 12, 1, 0, TimeSpan.FromHours(8)),
                Items = _summaryBases.GetValueOrDefault((type, id), []).Select(row => DemandSummaryRow(type, id, row)).ToList()
            }));
            Receipt = (id, _) => Task.FromResult(ServiceResult<DemandTrackingReceiptSectionDto>.Ok(new()
            {
                GrossDemandId = id,
                PurchaseLines = [new() { PurchaseLineId = SharedPurchaseLineId, PurchaseOrderNo = "PO202610001", PurchaseLineNumber = 1, Sku = "M-1001", PurchaseLineQty = 1000, ReceivedQty = 500, RemainingQty = 500, Unit = "PCS", IsSharedPurchaseLine = true }],
                Items = [new() { InboundLineId = Guid.NewGuid(), InboundDocumentNo = "IN202610001", LineNumber = 1, PurchaseLineId = SharedPurchaseLineId, PurchaseOrderNo = "PO202610001", PurchaseLineNumber = 1, Sku = "M-1001", Qty = 500, Unit = "PCS", PostingStatus = InboundPostingStatusEnum.Posted, CountsTowardReceipt = true, ReceiptDate = new DateTime(2026, 10, 25) }, new() { InboundDocumentNo = "历史收货", Sku = "M-OLD", Qty = 30, Unit = "PCS", CountsTowardReceipt = null, ReviewReason = "收货头缺失" }]
            }));
            MaterialHeader = (id, _) => Task.FromResult(ServiceResult<MaterialTrackingDetailDto>.Ok(new()
            {
                NetDemandId = id, GrossDemandId = MaterialGrossDemandId, SourceType = "SO", SourceNo = "SO202610001", SourceLineNumber = 1, CustomerCode = "C1", CustomerSku = "C-M-1001", Color = "白色", Specification = "220V", ParentSku = "P-1001", BomPath = "P-1001 / M-1001", BomVersion = 2,
                Material = new() { NetDemandId = id, Sku = "M-1001", Description = "物料采用快照", NetQty = 240, GrossQty = 240, Unit = "PCS", DecompositionRevision = 2, IsDeleted = true, LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack }, RolledBackAt = new DateTime(2026, 10, 20), RolledBackBy = "示例用户", RolledBackReason = "历史修订回退"
            }));
            MaterialPurchase = (id, _) => Task.FromResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Ok(new()
            {
                NetDemandId = id, GrossDemandId = MaterialGrossDemandId, NetQty = 240, Unit = "PCS", AssociatedPurchaseQty = 240, RemainingToPurchaseQty = 0,
                Items = [new() { LinkId = Guid.NewGuid(), NetDemandId = id, PurchaseLineId = SharedPurchaseLineId, PurchaseOrderNo = "PO202610001", PurchaseLineNumber = 1, Sku = "M-1001", SupplierName = "示例供应商", LinkedQty = 100, AllocatedQty = 100, HasPurchaseTarget = true, IsActiveLink = true, IsSharedPurchaseLine = true, PurchaseLineQty = 1000, Unit = "PCS" }, new() { LinkId = Guid.NewGuid(), NetDemandId = id, PurchaseLineId = SharedPurchaseLineId, PurchaseOrderNo = "PO202610001", PurchaseLineNumber = 1, Sku = "M-1001", LinkedQty = 140, AllocatedQty = 140, HasPurchaseTarget = true, IsActiveLink = true, IsSharedPurchaseLine = true, PurchaseLineQty = 1000, Unit = "PCS" }]
            }));
            MaterialReceipt = async (id, token) =>
            {
                var result = await Receipt(MaterialGrossDemandId, token);
                result.Data.NetDemandId = id;
                return result;
            };
        }
        public Task<ServiceResult<MaterialTrackingDetailDto>> GetMaterialDetailAsync(Guid netDemandId, CancellationToken cancellationToken = default)
        { SliceCalls.Add(("material-header", netDemandId, cancellationToken)); return MaterialHeader(netDemandId, cancellationToken); }
        public Task<ServiceResult<MaterialTrackingPurchaseSectionDto>> GetMaterialPurchasesAsync(Guid netDemandId, CancellationToken cancellationToken = default)
        { SliceCalls.Add(("material-purchases", netDemandId, cancellationToken)); return MaterialPurchase(netDemandId, cancellationToken); }
        public Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetMaterialReceiptsAsync(Guid netDemandId, CancellationToken cancellationToken = default)
        { SliceCalls.Add(("material-receipts", netDemandId, cancellationToken)); return MaterialReceipt(netDemandId, cancellationToken); }
        public async Task<ServiceResult<OrderTrackingSourceDemandsDto>> GetSourceDemandsAsync(string sourceType, Guid sourceGroupId, CancellationToken cancellationToken = default)
        {
            Requests.Add((sourceType, sourceGroupId)); Tokens.Add(cancellationToken);
            var result = await Search(sourceType, sourceGroupId, cancellationToken);
            if (result.Success && result.Data is not null) _summaryBases[(sourceType, sourceGroupId)] = result.Data.Items;
            return result;
        }
        public Task<ServiceResult<OrderTrackingSourceSummaryBatchDto>> GetSourceSummariesAsync(OrderTrackingSourceSummaryRequest request, CancellationToken cancellationToken = default)
        { SummaryRequests.Add(request); SummaryTokens.Add(cancellationToken); return SourceSummary(request, cancellationToken); }
        public Task<ServiceResult<OrderTrackingSourceDemandSummariesDto>> GetSourceDemandSummariesAsync(string sourceType, Guid sourceGroupId, CancellationToken cancellationToken = default)
        { DemandSummaryRequests.Add((sourceType, sourceGroupId, cancellationToken)); return DemandSummary(sourceType, sourceGroupId, cancellationToken); }
        public Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request, CancellationToken cancellationToken = default) { SourceRequests.Add(request); SourceTokens.Add(cancellationToken); return SourceSearch(request, cancellationToken); }
        public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default) { CustomerTokens.Add(cancellationToken); return Customers(cancellationToken); }
        public Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default) { HeaderIds.Add(grossDemandId); HeaderTokens.Add(cancellationToken); return Header(grossDemandId, cancellationToken); }
        public Task<ServiceResult<List<DemandTrackingMaterialDto>>> GetMaterialsAsync(Guid grossDemandId, CancellationToken cancellationToken = default) { SliceCalls.Add(("materials", grossDemandId, cancellationToken)); return Material(grossDemandId, cancellationToken); }
        public Task<ServiceResult<List<DemandTrackingPurchaseDto>>> GetPurchasesAsync(Guid grossDemandId, CancellationToken cancellationToken = default) { SliceCalls.Add(("purchases", grossDemandId, cancellationToken)); return Purchase(grossDemandId, cancellationToken); }
        public Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetReceiptsAsync(Guid grossDemandId, CancellationToken cancellationToken = default) { SliceCalls.Add(("receipts", grossDemandId, cancellationToken)); return Receipt(grossDemandId, cancellationToken); }
        public Task<ServiceResult<DemandTrackingDeliverySectionDto>> GetDeliveriesAsync(Guid grossDemandId, CancellationToken cancellationToken = default) { SliceCalls.Add(("deliveries", grossDemandId, cancellationToken)); return Delivery(grossDemandId, cancellationToken); }
    }
}
