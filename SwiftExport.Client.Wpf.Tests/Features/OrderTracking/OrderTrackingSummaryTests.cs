using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.OrderTracking.DemandDetail.Models;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExportClient.Features.OrderTracking.Overview.Policies;
using SwiftExportClient.Features.OrderTracking.Overview.Views;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.OrderTracking;

public sealed partial class OrderTrackingTests
{
    /// <summary>摘要请求独立于基础加载，当前页批量一次，301条完整子表也只发一次摘要请求。</summary>
    [TestMethod]
    public void Summaries_BatchOnceAndDoNotBlockBaseFacts() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var pendingSource = new TaskCompletionSource<ServiceResult<OrderTrackingSourceSummaryBatchDto>>();
        var pendingDemand = new TaskCompletionSource<ServiceResult<OrderTrackingSourceDemandSummariesDto>>();
        f.Api.SourceSummary = (_, _) => pendingSource.Task; f.Api.DemandSummary = (_, _, _) => pendingDemand.Task;
        f.Api.Search = (type, id, _) => Task.FromResult(Demands(type, id, Enumerable.Range(0, 301).Select(i => Row("P-" + i, id)).ToArray()));
        await f.Overview.InitializeAsync(); Assert.AreEqual(2, f.Overview.Sources.Count); Assert.IsFalse(f.Overview.IsLoadingSources); Assert.IsTrue(f.Overview.Summaries.IsLoadingSources);
        Assert.AreEqual(1, f.Api.SummaryRequests.Count); Assert.AreEqual(2, f.Api.SummaryRequests[0].Sources.Count);
        await ChooseSource(f); Assert.AreEqual(301, f.Overview.LoadedDemandCount); Assert.IsFalse(f.Overview.IsLoading); Assert.IsTrue(f.Overview.Summaries.IsLoadingDemands);
        Assert.AreEqual(1, f.Api.DemandSummaryRequests.Count); Assert.AreEqual(1, f.Api.Requests.Count);
        f.Overview.Dispose(); Assert.IsTrue(f.Api.SummaryTokens[0].IsCancellationRequested); Assert.IsTrue(f.Api.DemandSummaryRequests[0].Token.IsCancellationRequested);
        pendingSource.SetResult(ServiceResult<OrderTrackingSourceSummaryBatchDto>.Fail("旧请求")); pendingDemand.SetResult(ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail("旧请求"));
        await f.Overview.Summaries.RefreshSourcesCommand.ExecutionTask!; await f.Overview.Summaries.RefreshDemandsCommand.ExecutionTask!;
        Assert.IsNull(f.Overview.Summaries.SourceError); Assert.IsNull(f.Overview.Summaries.DemandError);
    });

    /// <summary>整批失败可独立重试，不清空来源、需求、选择或基础字段。</summary>
    [TestMethod]
    public void Summaries_FailureRetryPreservesBaseRowsAndSelection() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var sourceSuccess = f.Api.SourceSummary; var demandSuccess = f.Api.DemandSummary;
        f.Api.SourceSummary = (_, _) => Task.FromResult(ServiceResult<OrderTrackingSourceSummaryBatchDto>.Fail("摘要暂不可用"));
        f.Api.DemandSummary = (_, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail("摘要暂不可用"));
        await f.Overview.InitializeAsync(); await ChooseSource(f); var source = f.Overview.SelectedSource; var row = f.Overview.Items[0]; f.Overview.SelectedItem = row;
        Assert.AreEqual(TrackingSummaryLoadState.Failed, source!.SummaryState); Assert.AreEqual(TrackingSummaryLoadState.Failed, row.SummaryState);
        Assert.AreEqual("加载失败", row.DeliveredText); Assert.AreEqual(2, f.Overview.LoadedDemandCount); Assert.AreEqual(120m, row.DemandQty);
        f.Api.SourceSummary = sourceSuccess; f.Api.DemandSummary = demandSuccess;
        await f.Overview.Summaries.RefreshSourcesCommand.ExecuteAsync(null); await f.Overview.Summaries.RefreshDemandsCommand.ExecuteAsync(null);
        Assert.AreSame(source, f.Overview.SelectedSource); Assert.AreSame(row, f.Overview.SelectedItem); Assert.AreSame(row, f.Overview.Items[0]);
        Assert.AreEqual(TrackingSummaryLoadState.Loaded, row.SummaryState); Assert.IsNull(f.Overview.Summaries.DemandError); Assert.AreEqual(1, f.Api.SourceRequests.Count); Assert.AreEqual(1, f.Api.Requests.Count);
    });

    /// <summary>较慢旧批次忽略取消后到达，也不能替换最新摘要。</summary>
    [TestMethod]
    public void Summaries_RejectLateBatchAndKeepLatest() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var success = f.Api.SourceSummary; var old = new TaskCompletionSource<ServiceResult<OrderTrackingSourceSummaryBatchDto>>();
        f.Api.SourceSummary = (_, _) => old.Task; await f.Overview.InitializeAsync(); var oldTask = f.Overview.Summaries.RefreshSourcesCommand.ExecutionTask!;
        f.Api.SourceSummary = success; await f.Overview.Summaries.RefreshSourcesCommand.ExecuteAsync(null); var accepted = f.Overview.Sources[0].Summary;
        Assert.IsTrue(f.Api.SummaryTokens[0].IsCancellationRequested); old.SetResult(ServiceResult<OrderTrackingSourceSummaryBatchDto>.Fail("旧批次失败")); await oldTask;
        Assert.AreSame(accepted, f.Overview.Sources[0].Summary); Assert.IsNull(f.Overview.Summaries.SourceError); Assert.IsFalse(f.Overview.Summaries.IsLoadingSources);
    });

    /// <summary>快速切换来源及切换会话均取消旧完整摘要，旧响应不污染新上下文。</summary>
    [TestMethod]
    public void Summaries_RejectPreviousSourceAndSession() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var success = f.Api.DemandSummary; var old = new TaskCompletionSource<ServiceResult<OrderTrackingSourceDemandSummariesDto>>();
        f.Api.DemandSummary = (_, _, _) => old.Task; await f.Overview.InitializeAsync(); await ChooseSource(f); var oldTask = f.Overview.Summaries.RefreshDemandsCommand.ExecutionTask!;
        f.Api.DemandSummary = success; await ChooseSource(f, 1); var accepted = f.Overview.Items[0]; Assert.IsNotNull(accepted.Summary);
        Assert.IsTrue(f.Api.DemandSummaryRequests[0].Token.IsCancellationRequested); old.SetResult(ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail("旧来源失败")); await oldTask;
        Assert.AreSame(accepted, f.Overview.Items[0]); Assert.IsNull(f.Overview.Summaries.DemandError);
        var session = new TaskCompletionSource<ServiceResult<OrderTrackingSourceDemandSummariesDto>>(); f.Api.DemandSummary = (_, _, _) => session.Task;
        var request = f.Overview.Summaries.RefreshDemandsCommand.ExecuteAsync(null);
        f.User.SetCurrentUser(new MeDto { UserId = "another", Permissions = [PermissionKeys.OrderTrackingView] });
        session.SetResult(ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail("上一会话")); await request;
        Assert.AreEqual(0, f.Overview.Items.Count); Assert.IsNull(accepted.Summary); Assert.IsNull(f.Overview.Summaries.DemandError);
    });

    /// <summary>摘要自己的来源身份不匹配或重复时拒绝整批，不接纳跨来源数据。</summary>
    [TestMethod]
    public void Summaries_RejectMismatchedItemAndDuplicateIdentity() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var sourceSuccess = f.Api.SourceSummary; var demandSuccess = f.Api.DemandSummary;
        f.Api.SourceSummary = async (request, token) => { var result = await sourceSuccess(request, token); result.Data.Items.Add(result.Data.Items[0]); return result; };
        await f.Overview.InitializeAsync(); Assert.IsNull(f.Overview.Sources[0].Summary); StringAssert.Contains(f.Overview.Summaries.SourceError!, "重复");
        f.Api.DemandSummary = async (type, id, token) => { var result = await demandSuccess(type, id, token); result.Data.Items[0].SourceGroupId = Guid.NewGuid(); return result; };
        await ChooseSource(f); Assert.AreEqual(2, f.Overview.Items.Count); Assert.IsTrue(f.Overview.Items.All(x => x.Summary is null)); StringAssert.Contains(f.Overview.Summaries.DemandError!, "身份");
        f.Api.DemandSummary = async (type, id, token) => { var result = await demandSuccess(type, id, token); result.Data.Items.Add(result.Data.Items[0]); return result; };
        await f.Overview.Summaries.RefreshDemandsCommand.ExecuteAsync(null); StringAssert.Contains(f.Overview.Summaries.DemandError!, "重复");
    });

    /// <summary>部分摘要缺失只标记缺失行，其他行保留可读摘要及全部基础行。</summary>
    [TestMethod]
    public void Summaries_MissingRowsRemainVisibleAndUnknown() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var sourceSuccess = f.Api.SourceSummary; var demandSuccess = f.Api.DemandSummary;
        f.Api.SourceSummary = async (request, token) => { var result = await sourceSuccess(request, token); result.Data.Items.RemoveAt(0); return result; };
        f.Api.DemandSummary = async (type, id, token) => { var result = await demandSuccess(type, id, token); result.Data.Items.RemoveAt(0); return result; };
        await f.Overview.InitializeAsync(); await ChooseSource(f);
        Assert.AreEqual(2, f.Overview.Sources.Count); Assert.AreEqual(TrackingSummaryLoadState.Failed, f.Overview.Sources[0].SummaryState); Assert.IsNotNull(f.Overview.Sources[1].Summary);
        Assert.AreEqual(2, f.Overview.Items.Count); Assert.AreEqual("加载失败", f.Overview.Items[0].DeliveredText); Assert.IsNotNull(f.Overview.Items[1].Summary);
    });

    /// <summary>摘要单位来自摘要响应；版本差异明确要求刷新且不自动重试。</summary>
    [TestMethod]
    public void Summaries_UseOwnUnitAndExposeVersionMismatch() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var success = f.Api.DemandSummary; var mismatch = false;
        f.Api.DemandSummary = async (type, id, token) => { var result = await success(type, id, token); foreach (var item in result.Data.Items) { item.Unit = "BOX"; if (mismatch) item.DemandVersion++; } return result; };
        await f.Overview.InitializeAsync(); await ChooseSource(f); var row = f.Overview.Items[0]; StringAssert.Contains(row.DeliveredText, "BOX"); Assert.AreEqual("PCS", row.Unit);
        mismatch = true; await f.Overview.Summaries.RefreshDemandsCommand.ExecuteAsync(null); Assert.AreEqual("摘要需刷新", row.DeliveredText); Assert.AreEqual("摘要需刷新", row.AttentionText);
        Assert.AreEqual(2, f.Api.DemandSummaryRequests.Count); mismatch = false; await f.Overview.Summaries.RefreshDemandsCommand.ExecuteAsync(null); StringAssert.Contains(row.DeliveredText, "BOX");
    });

    /// <summary>读取状态、未知、可信零与不适用必须分别展示，活动物料核实不因采购不适用而丢失。</summary>
    [TestMethod]
    public void SummaryRows_DistinguishZeroUnknownForbiddenFailedAndInactive() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); await f.Overview.InitializeAsync(); await ChooseSource(f); var row = f.Overview.Items[0]; var summary = row.Summary!;
        summary.DeliveredQty = 0; summary.RemainingQty = null; summary.IsOverdue = null; summary.PurchaseStatus = "NotApplicable"; summary.MaterialReviewCount = 2;
        StringAssert.Contains(row.DeliveredText, "0.00"); Assert.AreEqual("待核实", row.RemainingText); Assert.AreEqual("待核实", row.OverdueText); Assert.AreEqual("不适用", row.PurchaseText); StringAssert.Contains(row.MaterialText, "核实 2");
        summary.DeliveryAvailability = "Forbidden"; summary.DeliveredQty = 999; Assert.AreEqual("无权", row.DeliveredText);
        summary.DeliveryAvailability = "Failed"; summary.DeliveryMessage = "交付读取暂不可用"; Assert.AreEqual("加载失败", row.DeliveredText); StringAssert.Contains(row.SummaryInfo, "交付读取暂不可用"); summary.DeliveryAvailability = "future"; Assert.AreEqual("未知", row.DeliveredText);
        summary.DeliveryAvailability = "Readable"; summary.IsActive = false; Assert.AreEqual("不适用（非活动）", row.DeliveredText); Assert.AreEqual("不适用（非活动）", row.PurchaseText); Assert.AreEqual("不适用（非活动）", row.ReceiptText);
    });

    /// <summary>来源整行收货采用服务端去重结论，不能累加子行重复关联数。</summary>
    [TestMethod]
    public void SummaryRows_KeepServerDeduplicatedCountsAndSeparateReadTime() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); await f.Overview.InitializeAsync(); await ChooseSource(f); var source = f.Overview.Sources[0];
        Assert.AreEqual(1, source.Summary!.ReceiptPurchaseLineCount); Assert.AreEqual(2, f.Overview.Items.Sum(x => x.Summary!.ReceiptPurchaseLineCount));
        StringAssert.Contains(source.ReceiptText, "未收足 1"); StringAssert.Contains(source.SummaryInfo, "+08:00"); StringAssert.Contains(source.SummaryInfo, "与基础查询独立");
        source.Summary.ReceiptCompletedLineCount = null; StringAssert.Contains(source.ReceiptText, "待核实"); Assert.AreNotEqual(f.Overview.Summaries.SourceStamp, f.Overview.Summaries.DemandStamp);
    });

    /// <summary>关注候选按各维度精确授权，采购或收货权限不能替代物料权限。</summary>
    [TestMethod]
    public void AttentionPolicy_RequiresExactDimensionPermissions() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); CollectionAssert.AreEquivalent(new[] { OrderTrackingAttentionCodes.MissingRequiredDate, OrderTrackingAttentionCodes.SourceReview }, f.Overview.AttentionOptions.Select(x => x.Value).ToArray());
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ProcurementOrderRead, PermissionKeys.InventoryInboundView] });
        Assert.AreEqual(2, f.Overview.AttentionOptions.Count);
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.DemandPlanningMaterialView, PermissionKeys.ProcurementOrderRead] });
        Assert.AreEqual(6, f.Overview.AttentionOptions.Count); Assert.IsFalse(f.Overview.AttentionOptions.Any(x => x.Value.StartsWith("Receipt")));
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.DemandPlanningMaterialView, PermissionKeys.ProcurementOrderRead, PermissionKeys.InventoryInboundView, PermissionKeys.ShippingPackingListView] });
        Assert.AreEqual(11, f.Overview.AttentionOptions.Count);
    });

    /// <summary>关注并集与其他基础条件同时提交，清空不保留选择且完整子表不继承条件。</summary>
    [TestMethod]
    public void AttentionRequest_FreezesUnionAlongsideBaseFiltersAndClears() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var vm = f.Overview;
        vm.Keyword = "needle"; vm.SelectedCustomerCodes.Add("C1"); vm.RequiredDateFrom = new(2026, 10, 1); vm.RequiredDateTo = new(2026, 11, 1);
        vm.SelectedAttentions.Add(vm.AttentionOptions.Single(x => x.Value == OrderTrackingAttentionCodes.Overdue)); vm.SelectedAttentions.Add(vm.AttentionOptions.Single(x => x.Value == OrderTrackingAttentionCodes.PurchaseShortfall));
        await vm.SearchCommand.ExecuteAsync(null); var request = f.Api.SourceRequests.Single();
        CollectionAssert.AreEquivalent(new[] { OrderTrackingAttentionCodes.Overdue, OrderTrackingAttentionCodes.PurchaseShortfall }, request.AttentionCodes); Assert.AreEqual("needle", request.Keyword); CollectionAssert.AreEqual(new[] { "C1" }, request.CustomerCodes); Assert.IsNotNull(request.RequiredDateFrom);
        await ChooseSource(f); Assert.AreEqual(vm.SelectedSource!.SourceGroupId, f.Api.DemandSummaryRequests.Single().Id); Assert.AreEqual(2, vm.LoadedDemandCount);
        await vm.ClearFiltersCommand.ExecuteAsync(null); Assert.AreEqual(0, vm.SelectedAttentions.Count); Assert.AreEqual(0, f.Api.SourceRequests.Last().AttentionCodes.Count); Assert.AreEqual(2, request.AttentionCodes.Count);
    });

    /// <summary>即使替身带回受限字段，本地权限仍遮住数量和关注标签。</summary>
    [TestMethod]
    public void SummaryRows_LocalPermissionsHideRestrictedValues() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Overview.InitializeAsync(); await ChooseSource(f);
        var row = f.Overview.Items[0]; Assert.IsNotNull(row.Summary); row.Summary.DeliveryMessage = "禁止展示的交付明细"; Assert.IsFalse(row.SummaryInfo.Contains("禁止展示")); Assert.AreEqual("无权", row.DeliveredText); Assert.AreEqual("无权", row.MaterialText); Assert.AreEqual("无权", row.PurchaseText); Assert.AreEqual("无权", row.ReceiptText);
        Assert.AreEqual("无可见关注项", row.AttentionText); Assert.AreEqual("无权", f.Overview.Sources[0].ReceiptText);
    });

    /// <summary>从总览跳转已打开需求时仍激活指定区，稳定需求身份只保留一个文档页签。</summary>
    [TestMethod]
    public void SummaryNavigation_ActivatesRequestedRegionOnExistingDocument() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); await f.Overview.InitializeAsync(); await ChooseSource(f); f.Overview.SelectedItem = f.Overview.Items[0];
        await f.Overview.OpenSelectedCommand.ExecuteAsync(null); var tab = f.Navigation.SelectedTab; var detail = (DemandTrackingDetailViewModel)tab!.ContentViewModel!;
        Assert.AreEqual(0, detail.SelectedSectionIndex); await f.Overview.OpenDemandSectionCommand.ExecuteAsync(DemandTrackingSection.Receipts);
        Assert.AreSame(tab, f.Navigation.SelectedTab); Assert.AreEqual(3, detail.SelectedSectionIndex); Assert.IsTrue(detail.Receipts.IsLoaded);
        await f.Overview.OpenDemandSectionCommand.ExecuteAsync(DemandTrackingSection.Deliveries); Assert.AreSame(tab, f.Navigation.SelectedTab); Assert.AreEqual(4, detail.SelectedSectionIndex); Assert.IsTrue(detail.Deliveries.IsLoaded); Assert.AreEqual(1, f.Navigation.OpenTabs.Count);
    });

    /// <summary>首次直接打开指定区加载该区；摘要额外权限不改变既有明细区域权限。</summary>
    [TestMethod]
    public void SummaryNavigation_NewRequestLoadsRegionWithOriginalPermissions() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ProcurementOrderRead] });
        await f.Overview.InitializeAsync(); await ChooseSource(f); f.Overview.SelectedItem = f.Overview.Items[0];
        Assert.AreEqual("无权", f.Overview.SelectedItem.PurchaseText); Assert.IsTrue(f.Overview.OpenDemandSectionCommand.CanExecute(DemandTrackingSection.Purchases)); Assert.IsFalse(f.Overview.OpenDemandSectionCommand.CanExecute(DemandTrackingSection.Receipts));
        await f.Overview.OpenDemandSectionCommand.ExecuteAsync(DemandTrackingSection.Purchases); var detail = (DemandTrackingDetailViewModel)f.Navigation.SelectedTab!.ContentViewModel!;
        Assert.AreEqual(2, detail.SelectedSectionIndex); Assert.IsTrue(detail.Purchases.IsLoaded); Assert.AreEqual(1, f.Api.SliceCalls.Count); Assert.AreEqual("purchases", f.Api.SliceCalls[0].Area);
    });

    /// <summary>实际摘要列支持本地筛选且不改变完整子表；请求失败后基础行与选择仍保留。</summary>
    [TestMethod]
    public void SummaryGrid_FiltersVisibleRowsAndKeepsFactsOnFailure() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); var success = f.Api.DemandSummary;
        f.Api.DemandSummary = async (type, id, token) => { var result = await success(type, id, token); result.Data.Items[1].AttentionCodes = []; return result; };
        await f.Overview.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = f.Overview };
        using var output = new StringWriter(); using var listener = new TextWriterTraceListener(output); var trace = PresentationTraceSources.DataBindingSource; var previous = trace.Switch.Level; trace.Switch.Level = SourceLevels.Error; trace.Listeners.Add(listener);
        try
        {
            await WithWindow(view, async () =>
            {
                var sources = (DataGrid)view.FindName("SourcesGrid"); sources.SelectedItem = f.Overview.Sources[0]; await Settle(); var grid = (DataGrid)view.FindName("TrackingGrid");
                Assert.AreEqual(2, grid.Items.Count); SavePreview(view, "summary-overview.png");
                var (filter, _) = await OpenFilter(grid, "关注事项"); var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter); choices.UnselectAll(); choices.SelectedItems.Add("逾期"); await Settle(); await Settle(); filter.IsPopupOpen = false;
                Assert.AreEqual(1, grid.Items.Count); Assert.AreEqual(2, f.Overview.LoadedDemandCount); Assert.AreEqual(1, f.Overview.VisibleDemandCount); SavePreview(view, "summary-attention-filter.png");
                f.Api.DemandSummary = (_, _, _) => Task.FromResult(ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail("摘要失败")); await f.Overview.Summaries.RefreshDemandsCommand.ExecuteAsync(null); await Settle();
                Assert.AreEqual(2, f.Overview.Items.Count); Assert.IsNotNull(f.Overview.SelectedSource); Assert.AreEqual("加载失败", f.Overview.Items[0].DeliveredText);
            });
            Assert.AreEqual("", output.ToString(), "摘要模板不能产生绑定错误。");
        }
        finally { trace.Listeners.Remove(listener); trace.Switch.Level = previous; }
    });
}
