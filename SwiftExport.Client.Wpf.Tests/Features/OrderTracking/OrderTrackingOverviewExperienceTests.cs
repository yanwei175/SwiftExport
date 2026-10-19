using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExportClient.Features.OrderTracking.Overview.Views;
using SwiftExportClient.Resources.Commands;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;

namespace SwiftExport.Client.Wpf.Tests.Features.OrderTracking;
public sealed partial class OrderTrackingTests
{
    /// <summary>编辑各条件保留两表和选择，刷新/分页只使用深复制的已应用条件。</summary>
    [TestMethod]
    public void AppliedQuery_DraftEditsKeepFactsAndRefreshUsesAcceptedSnapshot() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var vm = f.Overview; vm.Keyword = "applied"; vm.SelectedCustomerCodes.Add("C1"); await vm.InitializeAsync(); await ChooseSource(f);
        var source = vm.SelectedSource; var row = vm.Items[0]; vm.SelectedItem = row;
        vm.Keyword = "draft"; vm.SelectedSourceType = "CR"; vm.RequiredDateFrom = new(2026, 1, 1); vm.SelectedCustomerCodes.Clear(); vm.SelectedCustomerCodes.Add("C2"); vm.SelectedAttentions.Add(vm.AttentionOptions[0]);
        Assert.AreSame(source, vm.SelectedSource); Assert.AreSame(row, vm.SelectedItem); Assert.AreEqual(2, vm.Items.Count); Assert.IsTrue(vm.HasUnappliedChanges); StringAssert.Contains(vm.QueryStateText, "尚未查询");
        await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("applied", f.Api.SourceRequests.Last().Keyword); CollectionAssert.AreEqual(new[] { "C1" }, f.Api.SourceRequests.Last().CustomerCodes); Assert.AreEqual(0, f.Api.SourceRequests.Last().AttentionCodes.Count);
        Assert.AreSame(source, vm.SelectedSource); Assert.IsTrue(vm.HasUnappliedChanges); await vm.SourcePageChangeCommand.ExecuteAsync(Change(2)); Assert.AreEqual("applied", f.Api.SourceRequests.Last().Keyword);
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(1, vm.SourcePageIndex); Assert.AreEqual("draft", f.Api.SourceRequests.Last().Keyword); CollectionAssert.AreEqual(new[] { "CR" }, f.Api.SourceRequests.Last().SourceTypes); Assert.IsFalse(vm.HasUnappliedChanges);
    });

    /// <summary>失败查询及日期校验不接纳新条件，也不清除旧命中和独立重试上下文。</summary>
    [TestMethod]
    public void AppliedQuery_FailureKeepsAcceptedSnapshotAndMatches() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var vm = f.Overview; vm.Keyword = "old"; await vm.InitializeAsync(); await ChooseSource(f); vm.SelectedItem = vm.Items[0]; var source = vm.SelectedSource; var row = vm.SelectedItem;
        var sourceSuccess = f.Api.SourceSearch; f.Api.SourceSearch = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Fail("新查询失败")); vm.Keyword = "new";
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreSame(source, vm.SelectedSource); Assert.AreSame(row, vm.SelectedItem); Assert.AreEqual(true, row!.QueryMatched); Assert.AreEqual(1, vm.Matches.MatchedCount); Assert.IsTrue(vm.Matches.RefreshCommand.CanExecute(null)); Assert.IsTrue(vm.HasUnappliedChanges);
        await vm.Matches.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("old", f.Api.MatchRequests.Last().Request.Keyword);
        vm.RequiredDateFrom = new(2027, 1, 1); vm.RequiredDateTo = new(2026, 1, 1); var count = f.Api.SourceRequests.Count; await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(count, f.Api.SourceRequests.Count); Assert.AreSame(row, vm.SelectedItem);
        f.Api.SourceSearch = sourceSuccess; await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("old", f.Api.SourceRequests.Last().Keyword); Assert.IsNull(f.Api.SourceRequests.Last().RequiredDateFrom); Assert.IsTrue(vm.HasUnappliedChanges);
    });

    /// <summary>刷新按稳定来源和需求身份恢复，复用行对象；来源不再在当前页时才清除。</summary>
    [TestMethod]
    public void Refresh_RestoresStableSourceAndDemandThenClearsMissingSource() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var vm = f.Overview; var id = f.Api.Groups[0].SourceGroupId; var rows = new[] { Row("P-1", id), Row("P-2", id) };
        f.Api.Search = (type, group, _) => Task.FromResult(Demands(type, group, rows)); await vm.InitializeAsync(); await ChooseSource(f); var source = vm.SelectedSource; var selected = vm.Items[1]; vm.SelectedItem = selected;
        rows[1].DemandQty = 777; await vm.RefreshCommand.ExecuteAsync(null); Assert.AreSame(source, vm.SelectedSource); Assert.AreSame(selected, vm.SelectedItem); Assert.AreEqual(777m, selected.DemandQty); Assert.AreEqual(2, f.Api.Requests.Count); Assert.AreEqual(2, f.Api.MatchRequests.Count);
        f.Api.SourceSearch = (_, _) => Task.FromResult(SourcePage(f.Api.Groups[1])); await vm.RefreshCommand.ExecuteAsync(null); Assert.IsNull(vm.SelectedSource); Assert.AreEqual(0, vm.Items.Count); Assert.IsNull(vm.Matches.MatchedCount);
    });

    /// <summary>命中只使用已应用条件，服务端身份即使不含关键词也必须标记，并保留完整301行。</summary>
    [TestMethod]
    public void Matches_UseAppliedSnapshotAndOnlyServerIdentitiesWithoutFiltering() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var vm = f.Overview; vm.Keyword = "needle"; vm.SelectedCustomerCodes.Add("C1");
        var id = f.Api.Groups[0].SourceGroupId; var rows = Enumerable.Range(0, 301).Select(i => Row("NOT-NEEDLE-" + i, id)).ToArray();
        f.Api.Search = (type, group, _) => Task.FromResult(Demands(type, group, rows));
        f.Api.Match = (type, group, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceMatchesDto>.Ok(new() { SourceType = type, SourceGroupId = group, BusinessDate = new(2026, 10, 20), MatchedGrossDemandIds = [rows[300].GrossDemandId] }));
        await vm.InitializeAsync(); vm.Keyword = "unsent"; vm.SelectedCustomerCodes.Clear(); await ChooseSource(f);
        Assert.AreEqual(301, vm.Items.Count); Assert.AreEqual(1, vm.Matches.MatchedCount); Assert.AreEqual(true, vm.Items[300].QueryMatched); Assert.AreEqual(false, vm.Items[0].QueryMatched);
        Assert.AreEqual("needle", f.Api.MatchRequests.Single().Request.Keyword); CollectionAssert.AreEqual(new[] { "C1" }, f.Api.MatchRequests.Single().Request.CustomerCodes);
        vm.SelectedCustomerCodes.Add("C2"); CollectionAssert.AreEqual(new[] { "C1" }, f.Api.MatchRequests.Single().Request.CustomerCodes);
        OrderTrackingDemandRowModel? located = null; vm.Matches.LocateRequested += row => located = row; vm.Matches.LocateNextCommand.Execute(null); Assert.AreSame(vm.Items[300], located);
    });

    /// <summary>命中失败不当作零，独立重试成功空集合才显示零，基础及摘要不受影响。</summary>
    [TestMethod]
    public void Matches_FailureRetryAndEmptyAreDistinct() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; f.Api.Match = (_, _, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceMatchesDto>.Fail("匹配暂不可用"));
        await vm.InitializeAsync(); await ChooseSource(f); Assert.AreEqual(2, vm.Items.Count); var row = vm.Items[0]; var summary = row.Summary;
        Assert.IsNull(vm.Matches.MatchedCount); Assert.AreEqual("定位失败", row.MatchText); Assert.IsFalse(vm.Matches.LocateNextCommand.CanExecute(null));
        f.Api.Match = (type, id, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceMatchesDto>.Ok(new() { SourceType = type, SourceGroupId = id, MatchedGrossDemandIds = [] }));
        await vm.Matches.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(0, vm.Matches.MatchedCount); Assert.AreEqual("未命中", row.MatchText); Assert.AreSame(summary, row.Summary); Assert.AreEqual(1, f.Api.Requests.Count); Assert.AreEqual(2, vm.Items.Count);
    });

    /// <summary>较慢的命中响应不能跨来源、查询或会话回写；新查询失败后旧上下文仍可重试。</summary>
    [TestMethod]
    public void Matches_CancelOnNewQueryAndRejectLateResponse() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; var pending = new TaskCompletionSource<ServiceResult<OrderTrackingSourceMatchesDto>>(); var success = f.Api.Match;
        f.Api.Match = (_, _, _, _) => pending.Task; await vm.InitializeAsync(); await ChooseSource(f); var old = vm.Matches.RefreshCommand.ExecutionTask!; var source = vm.SelectedSource;
        f.Api.SourceSearch = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Fail("查询失败")); vm.Keyword = "new"; await vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Api.MatchRequests[0].Token.IsCancellationRequested); Assert.IsTrue(vm.Matches.RefreshCommand.CanExecute(null));
        f.Api.Match = success; await vm.Matches.RefreshCommand.ExecuteAsync(null); pending.SetResult(ServiceResult<OrderTrackingSourceMatchesDto>.Fail("迟到失败")); await old;
        Assert.AreEqual(1, vm.Matches.MatchedCount); Assert.IsNull(vm.Matches.ErrorMessage); Assert.AreSame(source, vm.SelectedSource);
        var session = new TaskCompletionSource<ServiceResult<OrderTrackingSourceMatchesDto>>(); f.Api.Match = (_, _, _, _) => session.Task; var load = vm.Matches.RefreshCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new MeDto { UserId = "other", Permissions = [PermissionKeys.OrderTrackingView] }); session.SetResult(ServiceResult<OrderTrackingSourceMatchesDto>.Fail("旧会话")); await load; Assert.AreEqual(0, vm.Items.Count); Assert.IsNull(vm.Matches.ErrorMessage);
    });

    /// <summary>命中来源身份错误或重复身份作为失败；完整事实始终保留。</summary>
    [TestMethod]
    public void Matches_RejectWrongSourceAndDuplicateIds() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var success = f.Api.Match;
        f.Api.Match = async (type, id, query, token) => { var result = await success(type, id, query, token); result.Data.SourceType = "CR"; return result; };
        await f.Overview.InitializeAsync(); await ChooseSource(f); Assert.IsNull(f.Overview.Matches.MatchedCount); Assert.AreEqual(2, f.Overview.Items.Count);
        f.Api.Match = async (type, id, query, token) => { var result = await success(type, id, query, token); result.Data.MatchedGrossDemandIds.Add(result.Data.MatchedGrossDemandIds[0]); return result; };
        await f.Overview.Matches.RefreshCommand.ExecuteAsync(null); StringAssert.Contains(f.Overview.Matches.ErrorMessage!, "重复");
    });

    /// <summary>基础读取失败有独立重试，不重复刷新摘要接口；空数据与本地过滤空有不同说明。</summary>
    [TestMethod]
    public void DemandBase_IndependentRetryAndDistinctEmptyStates() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview; StringAssert.Contains(vm.DemandStateText, "请选择"); var success = f.Api.Search;
        f.Api.Search = (_, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceDemandsDto>.Fail("基础读取失败")); await vm.InitializeAsync(); await ChooseSource(f);
        StringAssert.Contains(vm.DemandStateText, "读取失败"); Assert.IsTrue(vm.LoadSelectedSourceCommand.CanExecute(null)); Assert.AreEqual(0, f.Api.DemandSummaryRequests.Count);
        f.Api.Search = success; await vm.LoadSelectedSourceCommand.ExecuteAsync(null); Assert.AreEqual(2, vm.Items.Count); Assert.IsNull(vm.DemandLoadError); Assert.AreEqual(1, f.Api.DemandSummaryRequests.Count);
        vm.VisibleDemandCount = 0; StringAssert.Contains(vm.DemandStateText, "列筛选无结果"); f.Api.Search = (type, id, _) => Task.FromResult(Demands(type, id)); await vm.LoadSelectedSourceCommand.ExecuteAsync(null); StringAssert.Contains(vm.DemandStateText, "没有非删除");
    });

    /// <summary>真实表格刷新仅触发一次子表和命中请求，保留筛选、选择、冻结产品列及可见数。</summary>
    [TestMethod]
    public void OverviewExperience_ActualRefreshPreservesSelectionAndFilters() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); var vm = f.Overview; var id = f.Api.Groups[0].SourceGroupId; var rows = new[] { Row("KEEP", id), Row("OTHER", id) };
        f.Api.Search = (type, group, _) => Task.FromResult(Demands(type, group, rows));
        var sourceSummary = f.Api.SourceSummary;
        f.Api.SourceSummary = async (request, token) => { var result = await sourceSummary(request, token); result.Data.Items[1].ActiveDemandCount = 0; return result; };
        await vm.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = vm };
        await WithWindow(view, async () =>
        {
            var sources = (DataGrid)view.FindName("SourcesGrid"); var grid = (DataGrid)view.FindName("TrackingGrid"); sources.SelectedItem = vm.Sources[0]; await Settle(); grid.SelectedItem = vm.Items[0]; var selected = vm.SelectedItem;
            Assert.AreEqual(4, grid.FrozenColumnCount); CollectionAssert.AreEqual(new[] { "图片", "产品编码", "客户型号", "中性型号" }, grid.Columns.OrderBy(x => x.DisplayIndex).Take(4).Select(x => (string)x.Header).ToArray());
            Assert.AreEqual(Visibility.Collapsed, grid.Columns.Single(x => Equals(x.Header, "客户")).Visibility); Assert.AreEqual(Visibility.Visible, sources.Columns.Single(x => Equals(x.Header, "客户 PO")).Visibility);
            var (filter, _) = await OpenFilter(grid, "产品编码"); var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter); choices.UnselectAll(); choices.SelectedItems.Add("KEEP"); await Settle(); await Settle(); filter.IsPopupOpen = false;
            Assert.AreEqual(1, grid.Items.Count);
            var (sourceFilter, _) = await OpenFilter(sources, "交付条数"); var sourceChoices = (ListBox)sourceFilter.Template.FindName("PART_ListBox", sourceFilter); sourceChoices.UnselectAll(); sourceChoices.SelectedItems.Add("完成 1 / 未完 1"); await Settle(); await Settle(); sourceFilter.IsPopupOpen = false;
            Assert.AreEqual(1, sources.Items.Count); Assert.IsNotNull(vm.SelectedSource);
            await vm.Summaries.RefreshSourcesCommand.ExecuteAsync(null); await Settle(); Assert.AreEqual(1, sources.Items.Count); Assert.IsNotNull(vm.SelectedSource);
            var baseCalls = f.Api.Requests.Count; var matchCalls = f.Api.MatchRequests.Count;
            vm.Keyword = "editing"; Assert.AreSame(selected, vm.SelectedItem); await vm.RefreshCommand.ExecuteAsync(null); await Settle();
            Assert.AreSame(selected, vm.SelectedItem); Assert.AreEqual(1, grid.Items.Count); Assert.AreEqual(2, vm.Items.Count); Assert.AreEqual(1, vm.VisibleDemandCount); Assert.AreEqual(baseCalls + 1, f.Api.Requests.Count); Assert.AreEqual(matchCalls + 1, f.Api.MatchRequests.Count);
            Assert.IsTrue(vm.HasUnappliedChanges); StringAssert.Contains(vm.DemandTitle, "C1"); SavePreview(view, "experience-overview.png");
            DataGridFilterCommands.Clear.Execute(grid); await Settle(); vm.Matches.LocateNextCommand.Execute(null); Assert.AreSame(vm.Items[0], vm.SelectedItem); Assert.AreEqual(2, grid.Items.Count);
        });
    });

    /// <summary>窄窗工具栏换行且不丢操作，右键四区域遵循原权限并提供禁用原因。</summary>
    [TestMethod]
    public void OverviewExperience_NarrowWindowAndContextPermissions() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Overview.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = f.Overview };
        var window = new Window { Width = 820, Height = 960, ShowInTaskbar = false, Content = view };
        try
        {
            window.Show(); await Settle(); var source = (DataGrid)view.FindName("SourcesGrid"); var grid = (DataGrid)view.FindName("TrackingGrid"); source.SelectedItem = f.Overview.Sources[0]; await Settle(); grid.SelectedItem = f.Overview.Items[0]; await Idle();
            var delivery = Descendants(view).OfType<Button>().Single(x => Equals(x.Content, "交付明细")); Assert.IsFalse(delivery.IsEnabled); StringAssert.Contains((string)delivery.ToolTip, "权限");
            var clear = Descendants(view).OfType<Button>().Single(x => Equals(x.Content, "清除成品需求列筛选")); Assert.IsTrue(clear.TranslatePoint(new Point(0, 0), view).X + clear.ActualWidth <= view.ActualWidth + 1);
            Assert.IsTrue(grid.ActualHeight > 60); Assert.IsTrue(grid.IsReadOnly); Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true; await Idle(); Assert.AreEqual(7, grid.ContextMenu.Items.OfType<MenuItem>().Count()); var item = grid.ContextMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "交付明细")); Assert.IsFalse(item.IsEnabled); grid.ContextMenu.IsOpen = false;
            var date = Descendants(view).OfType<DatePicker>().First(); var label = Descendants(view).OfType<TextBlock>().Single(x => x.Text == "要求交期起");
            Assert.IsTrue(Math.Abs(date.TranslatePoint(new Point(0, date.ActualHeight / 2), view).Y - label.TranslatePoint(new Point(0, label.ActualHeight / 2), view).Y) < 6, "日期标签与输入必须一起换行。");
            SavePreview(view, "experience-narrow.png");
        }
        finally { window.Close(); await Idle(); }
    });
    /// <summary>滚动到完整集合深处后刷新保留选择与滚动，横向滚动不移走冻结产品列。</summary>
    [TestMethod]
    public void OverviewExperience_RefreshKeepsDeepScrollAndHiddenMatchDoesNotFilter() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); var vm = f.Overview; var id = f.Api.Groups[0].SourceGroupId; var rows = Enumerable.Range(0, 301).Select(i => Row("P-" + i, id)).ToArray();
        f.Api.Search = (type, group, _) => Task.FromResult(Demands(type, group, rows));
        var sourceSummary = f.Api.SourceSummary;
        f.Api.SourceSummary = async (request, token) => { var result = await sourceSummary(request, token); result.Data.Items[1].ActiveDemandCount = 0; return result; };
        await vm.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = vm };
        await WithWindow(view, async () =>
        {
            var sources = (DataGrid)view.FindName("SourcesGrid"); sources.SelectedItem = vm.Sources[0]; await Settle(); var grid = (DataGrid)view.FindName("TrackingGrid"); grid.SelectedItem = vm.Items[250]; grid.ScrollIntoView(vm.Items[250]); await Settle();
            var scroll = Descendants(grid).OfType<ScrollViewer>().First(); var offset = scroll.VerticalOffset; Assert.IsTrue(offset > 100);
            var selected = vm.SelectedItem; await vm.RefreshCommand.ExecuteAsync(null); await Settle(); Assert.AreSame(selected, vm.SelectedItem); Assert.IsTrue(Math.Abs(offset - scroll.VerticalOffset) < 2, $"刷新前{offset}，刷新后{scroll.VerticalOffset}");
            grid.ScrollIntoView(selected, grid.Columns.Single(x => Equals(x.Header, "关联采购整行"))); await Settle(); Assert.IsTrue(grid.Columns.Where(x => x.DisplayIndex < 4).All(x => x.IsFrozen));
            var (filter, _) = await OpenFilter(grid, "产品编码"); var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter); choices.UnselectAll(); choices.SelectedItems.Add("P-250"); await Settle(); await Settle(); filter.IsPopupOpen = false;
            vm.Matches.LocateNextCommand.Execute(null); StringAssert.Contains(vm.Matches.LocationMessage!, "列筛选隐藏"); Assert.AreEqual(301, vm.Items.Count); Assert.AreEqual(1, grid.Items.Count);
            await Settle(); SavePreview(view, "experience-frozen-filter.png");
        });
    });}
