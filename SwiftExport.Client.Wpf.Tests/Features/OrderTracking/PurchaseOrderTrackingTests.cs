using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using DataGridExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.Models;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.OrderTracking.PurchaseOrders.Views;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Features.OrderTracking;

/// <summary>验证采购单独立视角、查询快照、完整证据及维度权限，不通过需求或管理API绕行。</summary>
[TestClass, DoNotParallelize]
public sealed class PurchaseOrderTrackingTests
{
    /// <summary>采购跟踪的行、反馈和收货子表均在窄窗口内横向滚动，次要列可从查看菜单恢复。</summary>
    [TestMethod]
    public void Tracking_NarrowEvidenceGridsCanScrollAndRestoreSecondaryColumns() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Vm.InitializeAsync();
        f.Vm.Lines[0].Data.SourceSummary = "独立采购"; f.Vm.Lines[0].Data.SubNote = "从查看菜单恢复的备注";
        var screenshotLines = f.Vm.Lines.Select(x => x.Data).ToList();
        f.LinesHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseOrderTrackingLineDto>>.Ok(screenshotLines));
        var view = new PurchaseOrderTrackingView { DataContext = f.Vm };
        var window = new Window { Content = view, Width = 800, Height = 1300, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var lines = (DataGrid)view.FindName("LinesGrid");
            CollectionAssert.AreEqual(new[] { "图片", "行", "SKU", "客户型号", "描述", "采购数量", "单位", "计划交期", "累计货好", "预计完成", "正式净收货", "正式未收", "收货核实", "来源概况" },
                lines.Columns.Where(x => x.Visibility == Visibility.Visible).Select(x => x.Header!.ToString()).ToArray());
            await DataGridHorizontalAccessAssertions.ReachRightmostColumnAsync(lines, view.ActualWidth);
            await DataGridHorizontalAccessAssertions.RestoreColumnFromMenuAsync(lines, "备注");
            await DataGridHorizontalAccessAssertions.ReachRightmostColumnAsync(lines, view.ActualWidth);
            await DataGridHorizontalAccessAssertions.ReachRightmostColumnAsync((DataGrid)view.FindName("ReportsGrid"), view.ActualWidth);
            var tabs = Descendants(view).OfType<TabControl>().Single(); tabs.SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            foreach (var name in new[] { "ReceiptLinesGrid", "ReceiptsGrid" })
                await DataGridHorizontalAccessAssertions.ReachRightmostColumnAsync((DataGrid)view.FindName(name), view.ActualWidth);
            DataGridHorizontalAccessAssertions.SaveScreenshot(view, "purchase-tracking-narrow-right.png");
        }
        finally { window.Close(); }
    });

    /// <summary>独立创建和需求创建均可查询，主表分页不裁剪302条全量采购行，初始化幂等。</summary>
    [TestMethod]
    public async Task IndependentAndLinkedOrders_LoadFullLinesAndCompleteEvidence()
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync(); await f.Vm.InitializeAsync();
        Assert.AreEqual(1, f.Searches.Count); Assert.AreEqual(2, f.Vm.Orders.Count); Assert.AreEqual("独立创建", f.Vm.Orders[0].OriginText);
        Assert.AreEqual("需求创建", f.Vm.Orders[1].OriginText); Assert.AreEqual(302, f.Vm.Lines.Count);
        Assert.AreEqual(1, f.ReportCalls); Assert.AreEqual(1, f.ReceiptCalls); Assert.AreEqual(2, f.Vm.Reports.Count);
        Assert.IsTrue(f.Vm.IsReadOnly); Assert.IsFalse(f.Vm.HasUnsavedChanges); Assert.IsTrue(await f.Vm.CanCloseAsync(CancellationToken.None));
        Assert.AreEqual(4m, f.Vm.Reports[0].Data.ReadyQty); Assert.AreEqual(8m, f.Vm.Reports[1].Data.ReadyQty);
        Assert.IsTrue(f.Vm.Reports[0].Data.IsCorrection); Assert.AreEqual(2m, f.Vm.ReceiptLines.Single().ReceivedQty);
        await f.Vm.OpenFactsCommand.ExecuteAsync(null); await f.Vm.OpenFactsCommand.ExecuteAsync(null);
        Assert.AreEqual(f.Navigation.Keys[0], f.Navigation.Keys[1]);
        Assert.AreEqual($"purchase-order:new:{f.First.PurchaseOrderId:N}", f.Navigation.Keys[0]);
    }

    /// <summary>已应用条件冻结，翻页不混入尚未查询的输入，失败不提交页码或丢弃旧结果。</summary>
    [TestMethod]
    public async Task AppliedQuery_PagingAndFailuresPreserveAcceptedSnapshot()
    {
        using var f = new Fixture(); f.Vm.Keyword = "PO-old"; f.Vm.SelectedCustomers.Add(new() { CustomerCode = "C1" });
        await f.Vm.InitializeAsync(); f.Vm.Keyword = "unapplied"; f.Vm.SelectedCustomers.Clear();
        await f.Vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 40 });
        Assert.AreEqual("PO-old", f.Searches.Last().Keyword); CollectionAssert.AreEqual(new[] { "C1" }, f.Searches.Last().CustomerCodes);
        Assert.AreEqual(2, f.Vm.PageIndex); var selected = f.Vm.SelectedOrder;
        f.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>.Fail("读取失败"));
        await f.Vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 3, NewSize = 80 });
        Assert.AreEqual(2, f.Vm.PageIndex); Assert.AreSame(selected, f.Vm.SelectedOrder); Assert.AreEqual(302, f.Vm.Lines.Count);
        await f.Vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("PO-old", f.Searches.Last().Keyword);
    }

    /// <summary>新查询取消旧查询，旧分页响应即使忽略取消也不回写。</summary>
    [TestMethod]
    public async Task LateSearch_IsIgnoredAfterNewerSearch()
    {
        using var f = new Fixture(); var late = new TaskCompletionSource<ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>>(); CancellationToken firstToken = default;
        f.SearchHandler = (_, token) => { firstToken = token; return late.Task; };
        var old = f.Vm.SearchCommand.ExecuteAsync(null); f.SearchHandler = (r, _) => Task.FromResult(f.Page(r, [f.Second]));
        await f.Vm.SearchCommand.ExecuteAsync(null); Assert.IsTrue(firstToken.IsCancellationRequested);
        late.SetResult(f.Page(new(), [f.First])); await old;
        Assert.AreEqual(f.Second.PurchaseOrderId, f.Vm.Orders.Single().Data.PurchaseOrderId);
    }

    /// <summary>切单与释放均取消全量行读取，迟到的旧证据不能覆盖当前采购。</summary>
    [TestMethod]
    public async Task LateDetails_AfterSelectionAndDisposeDoNotOverwrite()
    {
        using var f = new Fixture(); var late = new TaskCompletionSource<ServiceResult<List<PurchaseOrderTrackingLineDto>>>(); CancellationToken token = default;
        f.LinesHandler = (id, ct) => { if (id == f.First.PurchaseOrderId) { token = ct; return late.Task; } return Task.FromResult(ServiceResult<List<PurchaseOrderTrackingLineDto>>.Ok([new() { Sku = "current" }])); };
        var opening = f.Vm.InitializeAsync(); f.Vm.SelectedOrder = f.Vm.Orders[1]; await f.Vm.LoadSelectedOrderCommand.ExecuteAsync(null);
        Assert.IsTrue(token.IsCancellationRequested); late.SetResult(ServiceResult<List<PurchaseOrderTrackingLineDto>>.Ok([new() { Sku = "old" }])); await opening;
        Assert.AreEqual("current", f.Vm.Lines.Single().Data.Sku);
        var closing = new TaskCompletionSource<ServiceResult<List<PurchaseOrderTrackingLineDto>>>(); f.LinesHandler = (_, _) => closing.Task;
        var reload = f.Vm.LoadSelectedOrderCommand.ExecuteAsync(null); f.Vm.Dispose(); closing.SetResult(ServiceResult<List<PurchaseOrderTrackingLineDto>>.Ok([new() { Sku = "disposed" }])); await reload;
        Assert.AreEqual("current", f.Vm.Lines.Single().Data.Sku);
    }

    /// <summary>缺少进度维度权限不读对应证据，不能使用对应条件；仍可看采购事实和独立采购。</summary>
    [TestMethod]
    public async Task DimensionPermissions_DoNotLeakOrCallForbiddenEndpoints()
    {
        using var f = new Fixture(false); f.First.CanViewProgress = false; f.First.CanViewReceipts = false;
        await f.Vm.InitializeAsync(); Assert.AreEqual(0, f.ReportCalls); Assert.AreEqual(0, f.ReceiptCalls);
        Assert.AreEqual("无权", f.Vm.Orders[0].ReadyText); Assert.AreEqual("无权", f.Vm.Orders[0].ReceiptText);
        f.Vm.SelectedReadyStatuses.Add(PurchaseReadyStatusEnum.FullyReady); await f.Vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(1, f.Searches.Count);
        f.Vm.SelectedReadyStatuses.Clear(); f.Vm.SelectedReceiptStatuses.Add(PurchaseReceiptStatusEnum.FullyReceived); await f.Vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(1, f.Searches.Count);
        f.User.SetCurrentUser(new() { UserId = "u", Permissions = [] });
        Assert.AreEqual(0, f.Vm.Orders.Count); Assert.AreEqual(0, f.Vm.Lines.Count); Assert.IsFalse(f.Vm.SearchCommand.CanExecute(null));
        await f.Vm.OpenFactsCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Navigation.Keys.Count);
    }

    /// <summary>某维度失败不清空成功维度，不将供应商货好量当作正式收货。</summary>
    [TestMethod]
    public async Task EvidenceFailure_IsIndependentFromFormalReceiptAndLineFacts()
    {
        using var f = new Fixture(); f.ReportsFail = true; await f.Vm.InitializeAsync();
        Assert.AreEqual(302, f.Vm.Lines.Count); Assert.AreEqual(0, f.Vm.Reports.Count); StringAssert.Contains(f.Vm.ReportsMessage, "反馈失败");
        Assert.AreEqual(2m, f.Vm.ReceiptLines.Single().ReceivedQty); Assert.AreEqual("2", f.Vm.Lines[0].ReceivedText); Assert.AreEqual("9", f.Vm.Lines[0].ReadyText);
        f.User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ProcurementOrderRead] });
        Assert.AreEqual(0, f.Vm.Receipts.Count); Assert.AreEqual(0, f.Vm.Reports.Count);
    }

    /// <summary>Runner未分类的单维异常也不能丢掉其他成功区域或使其停在加载中。</summary>
    [TestMethod]
    public async Task UnexpectedReportException_DoesNotDiscardOtherEvidence()
    {
        using var f = new Fixture(); f.ReportsThrow = true; await f.Vm.InitializeAsync();
        Assert.AreEqual(302, f.Vm.Lines.Count); Assert.AreEqual(1, f.Vm.Receipts.Count);
        Assert.AreEqual(2m, f.Vm.ReceiptLines.Single().ReceivedQty); StringAssert.Contains(f.Vm.ReportsMessage, "读取失败");
        Assert.IsFalse(f.Vm.IsLoadingDetails); Assert.IsFalse(f.Vm.ReportsMessage.Contains("正在读取"));
    }

    /// <summary>基础候选失败或抛异常不阻断首次采购查询，且保留明确的候选失败提示。</summary>
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task ReferenceCandidateFailure_DoesNotBlockOrderSearch(bool throws)
    {
        var references = DispatchProxy.Create<IReferenceDataCacheService, ApiProxy>();
        ((ApiProxy)(object)references).Handler = (method, _) => method!.Name == nameof(IReferenceDataCacheService.EnsureLoadedAsync)
            ? throws ? Task.FromException<CacheRefreshResult>(new IOException("offline")) : Task.FromResult(CacheRefreshResult.Fail("offline"))
            : throw new NotSupportedException();
        using var f = new Fixture(referenceData: references); await f.Vm.InitializeAsync(); await f.Vm.InitializeAsync();
        Assert.AreEqual(1, f.Searches.Count); Assert.AreEqual(2, f.Vm.Orders.Count); StringAssert.Contains(f.Vm.ReferenceDataMessage, "候选");
    }

    /// <summary>进度的未知、真实零、无权及作废不适用不能互相替代，负的正式未收保留异常事实。</summary>
    [TestMethod]
    public void QuantityAndStatusFacts_KeepNullZeroForbiddenAndNegativeDistinct()
    {
        var dto = new PurchaseOrderTrackingLineDto { CanViewProgress = true, CanViewReceipts = true, Quantity = 10, ReadyQty = null, ReceivedQty = null, UnreceivedQty = -2 };
        var row = new PurchaseOrderTrackingLineModel(dto); Assert.AreEqual("尚未反馈", row.ReadyText); Assert.AreEqual("需核实", row.ReceivedText); Assert.AreEqual("-2", row.RemainingText);
        dto.ReadyQty = 0; dto.ReceivedQty = 0; Assert.AreEqual("0", row.ReadyText); Assert.AreEqual("0", row.ReceivedText);
        dto.CanViewProgress = false; dto.CanViewReceipts = false; Assert.AreEqual("无权", row.ReadyText); Assert.AreEqual("无权", row.RemainingText);
        dto.AssociationStatus = PurchaseDemandAssociationStatusEnum.NotApplicable; Assert.AreEqual("不适用", row.LinkedText);
        var head = new PurchaseOrderTrackingItemDto { CanViewProgress = true, CanViewReceipts = true, ReadyStatus = PurchaseReadyStatusEnum.QuantityUnknown, ReceiptStatus = PurchaseReceiptStatusEnum.NeedsReview };
        var order = new PurchaseOrderTrackingRowModel(head); Assert.AreEqual("数量未知", order.ReadyText); Assert.AreEqual("需核实", order.ReceiptText);
        head.ReadyStatus = PurchaseReadyStatusEnum.FullyReady; Assert.AreEqual("全部货好", order.ReadyText);
        head.ReadyStatus = null; Assert.AreEqual("需核实", order.ReadyText); head.CanViewProgress = false; Assert.AreEqual("无权", order.ReadyText);
        head.CanViewProgress = true; head.HasUnconfirmedReadyRisk = true; StringAssert.Contains(order.RiskText, "货好量尚未反馈"); Assert.IsFalse(order.RiskText.Contains("采购未确认"));
    }

    /// <summary>正常未确认草稿不计待收；异常未确认收货仍须核实，无权优先隐藏维度。</summary>
    [TestMethod]
    public void UnconfirmedDraft_ReceiptNotApplicableIsDistinctFromEvidenceAnomaly()
    {
        var head = new PurchaseOrderTrackingItemDto { ConfirmationStatus = ConfirmationStatusEnum.待确认, CanViewReceipts = true };
        var detail = new PurchaseOrderTrackingLineDto { CanViewReceipts = true };
        var row = new PurchaseOrderTrackingRowModel(head); var line = new PurchaseOrderTrackingLineModel(detail, head);
        Assert.AreEqual("未确认，不计待收", row.ReceiptText); Assert.AreEqual("未确认，不计待收", row.ReceiptProgressText);
        Assert.AreEqual("未确认，不计待收", line.ReceivedText); Assert.AreEqual("未确认，不计待收", line.RemainingText);
        head.ReceiptStatus = PurchaseReceiptStatusEnum.NeedsReview; detail.ReceiptReviewReason = "未确认采购存在收货证据";
        Assert.AreEqual("需核实", row.ReceiptText); Assert.AreEqual("需核实", row.ReceiptProgressText);
        Assert.AreEqual("需核实", line.ReceivedText); Assert.AreEqual("需核实", line.RemainingText);
        head.CanViewReceipts = false; detail.CanViewReceipts = false;
        Assert.AreEqual("无权", row.ReceiptText); Assert.AreEqual("无权", line.ReceivedText);
    }

    /// <summary>真实页面所有表只读整行选择，保留原生筛选、列查看注入及采购单稳定导航。</summary>
    [TestMethod]
    public void RealView_UsesReadOnlyFullRowsAndExistingFilterMenus() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Vm.InitializeAsync(); var view = new PurchaseOrderTrackingView { DataContext = f.Vm };
        using var bindingErrors = new StringWriter(); using var listener = new TextWriterTraceListener(bindingErrors);
        var bindingLevel = PresentationTraceSources.DataBindingSource.Switch.Level; PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        var window = new Window { Content = view, Width = 1600, Height = 1100, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); view.UpdateLayout();
            var grids = Descendants(view).OfType<DataGrid>().ToArray(); Assert.IsTrue(grids.Length >= 3);
            foreach (var grid in grids) { Assert.IsTrue(grid.IsReadOnly); Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit); Assert.IsTrue(grid.GetIsAutoFilterEnabled()); Assert.IsTrue(grid.EnableColumnVirtualization); Assert.IsTrue(grid.ContextMenu.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "查看"))); }
            var orders = (DataGrid)view.FindName("OrdersGrid"); var lines = (DataGrid)view.FindName("LinesGrid");
            Assert.AreEqual(2, orders.Items.Count); Assert.AreEqual(302, lines.Items.Count);
            Assert.IsTrue(lines.Columns.Any(x => Equals(x.Header, "正式净收货"))); Assert.IsTrue(lines.Columns.Any(x => Equals(x.Header, "来源概况")));
            orders.SelectedItem = f.Vm.Orders[1]; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (f.Vm.LoadSelectedOrderCommand.ExecutionTask is { } load) await load;
            Assert.AreEqual(f.Second.PurchaseOrderId, f.LineRequests.Last());
            Assert.IsFalse(Descendants(view).OfType<Button>().Any(x => Equals(x.Content, "登记不可覆盖反馈") || Equals(x.Content, "确认采购")));
            await FilterOrder(orders, "PO-independent"); Assert.AreEqual(1, orders.Items.Count); Assert.AreEqual(2, f.Vm.Orders.Count);
            Assert.IsNull(f.Vm.SelectedOrder); Assert.AreEqual(0, f.Vm.Lines.Count);
            SwiftExportClient.Resources.Commands.DataGridFilterCommands.Clear.Execute(orders);
            orders.SelectedItem = f.Vm.Orders[0]; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (f.Vm.LoadSelectedOrderCommand.ExecutionTask is { } reload) await reload;
            var tabs = Descendants(view).OfType<TabControl>().Single(); tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); view.UpdateLayout();
            foreach (var grid in Descendants(view).OfType<DataGrid>()) Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            listener.Flush(); Assert.AreEqual(string.Empty, bindingErrors.ToString(), "新采购跟踪视图不能有绑定路径或类型错误。");
            // 页面在Shell内容区中继承浅色背景；离屏截图明确绘制该背景，避免透明区域显示成黑色。
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
            { drawing.DrawRectangle(Brushes.White, null, new Rect(view.RenderSize)); drawing.DrawRectangle(new VisualBrush(view), null, new Rect(view.RenderSize)); }
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var folder = Path.Combine(RepositoryRoot(), "artifacts", "ui"); Directory.CreateDirectory(folder);
            using var file = File.Create(Path.Combine(folder, "purchase-order-tracking.png")); encoder.Save(file);
        }
        finally { window.Close(); PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); PresentationTraceSources.DataBindingSource.Switch.Level = bindingLevel; }
    });

    private static async Task FilterOrder(DataGrid grid, string value)
    {
        var column = grid.Columns.Single(x => Equals(x.Header, "采购单号")); grid.ScrollIntoView(grid.Items[0], column);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); grid.UpdateLayout();
        var filter = Descendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single();
        ((IToggleProvider)new ToggleButtonAutomationPeer((ToggleButton)filter.Template.FindName("FilterButton", filter)).GetPattern(PatternInterface.Toggle)!).Toggle();
        await Task.Delay(250); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var list = (ListBox)filter.Template.FindName("PART_ListBox", filter); list.UnselectAll(); list.SelectedItems.Add(value); filter.IsPopupOpen = false;
        await Task.Delay(grid.GetFilterEvaluationDelay() + TimeSpan.FromMilliseconds(100)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static string RepositoryRoot()
    { for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName; throw new InvalidOperationException("找不到工作区。"); }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; } }

    private sealed class Fixture : IDisposable
    {
        public TrackingUser User { get; } = new();
        public TrackingNavigation Navigation { get; } = new();
        public PurchaseOrderTrackingItemDto First { get; } = new() { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-independent", BoNo = "PO-20261020-01", CustomerCode = "C100", SupplierName = "测试供应商", OriginKind = 2, CanViewProgress = true, CanViewReceipts = true,
            ConfirmationStatus = ConfirmationStatusEnum.已确认, TotalQty = 3020, UnitSnapshot = "PCS", ReadyQty = 9, ReadyStatus = PurchaseReadyStatusEnum.PartiallyReady, ReceiptStatus = PurchaseReceiptStatusEnum.PartiallyReceived, ReceiptProgress = 0.2m };
        public PurchaseOrderTrackingItemDto Second { get; } = new() { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-demand", CustomerCode = "C100", SupplierName = "测试供应商", OriginKind = 1, CanViewProgress = true, CanViewReceipts = true };
        private readonly Guid _line = Guid.NewGuid();
        public PurchaseOrderTrackingViewModel Vm { get; }
        public List<PurchaseOrderTrackingSearchRequest> Searches { get; } = [];
        public List<Guid> LineRequests { get; } = [];
        public int ReportCalls, ReceiptCalls; public bool ReportsFail, ReportsThrow;
        public Func<PurchaseOrderTrackingSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>>>? SearchHandler;
        public Func<Guid, CancellationToken, Task<ServiceResult<List<PurchaseOrderTrackingLineDto>>>>? LinesHandler;
        public Fixture(bool dimensions = true, IReferenceDataCacheService? referenceData = null)
        {
            if (!dimensions) User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ProcurementOrderRead] });
            var api = DispatchProxy.Create<IOrderTrackingApi, ApiProxy>(); ((ApiProxy)(object)api).Handler = (method, args) => method!.Name switch
            {
                nameof(IOrderTrackingApi.SearchPurchaseOrdersAsync) => Search((PurchaseOrderTrackingSearchRequest)args![0]!, (CancellationToken)args[1]!),
                nameof(IOrderTrackingApi.GetPurchaseOrderLinesAsync) => Lines((Guid)args![0]!, (CancellationToken)args[1]!),
                nameof(IOrderTrackingApi.GetPurchaseOrderReportsAsync) => Reports(),
                nameof(IOrderTrackingApi.GetPurchaseOrderReceiptsAsync) => Receipts((Guid)args![0]!),
                _ => throw new AssertFailedException("采购视角不得调用需求接口或写命令：" + method.Name)
            };
            Vm = new(api, new Runner(), User, Navigation, referenceData);
        }
        public ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>> Page(PurchaseOrderTrackingSearchRequest request, List<PurchaseOrderTrackingItemDto>? rows = null)
            => ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>.Ok(new() { Items = rows ?? [First, Second], TotalCount = 2, PageIndex = request.PageIndex > 0 ? request.PageIndex : 1, PageSize = request.PageSize > 0 ? request.PageSize : 20 });
        private Task<ServiceResult<PagedResponseBase<PurchaseOrderTrackingItemDto>>> Search(PurchaseOrderTrackingSearchRequest request, CancellationToken token)
        { Searches.Add(request); return SearchHandler?.Invoke(request, token) ?? Task.FromResult(Page(request)); }
        private Task<ServiceResult<List<PurchaseOrderTrackingLineDto>>> Lines(Guid id, CancellationToken token)
        {
            LineRequests.Add(id); return LinesHandler?.Invoke(id, token) ?? Task.FromResult(ServiceResult<List<PurchaseOrderTrackingLineDto>>.Ok(Enumerable.Range(0, 302).Select(i => new PurchaseOrderTrackingLineDto
            { PurchaseOrderLineId = i == 0 ? _line : Guid.NewGuid(), LineNumber = i + 1, Sku = "SKU-" + i, Quantity = 10, UnitSnapshot = "PCS", CanViewProgress = User.HasPermission(PermissionKeys.ProcurementProgressView), CanViewReceipts = User.HasPermission(PermissionKeys.InventoryInboundView), ReadyQty = 9, ReceivedQty = 2, UnreceivedQty = 8 }).ToList()));
        }
        private Task<ServiceResult<List<SupplierProgressReportDto>>> Reports()
        { ReportCalls++; if (ReportsThrow) return Task.FromException<ServiceResult<List<SupplierProgressReportDto>>>(new InvalidOperationException("unexpected")); return Task.FromResult(ReportsFail ? ServiceResult<List<SupplierProgressReportDto>>.Fail("反馈失败") : ServiceResult<List<SupplierProgressReportDto>>.Ok([new() { PurchaseOrderLineId = _line, Sequence = 1, ReadyQty = 8 }, new() { PurchaseOrderLineId = _line, Sequence = 2, ReadyQty = 4, IsCorrection = true, CorrectionReason = "实物复核" }])); }
        private Task<ServiceResult<PurchaseOrderTrackingReceiptsDto>> Receipts(Guid id)
        { ReceiptCalls++; return Task.FromResult(ServiceResult<PurchaseOrderTrackingReceiptsDto>.Ok(new() { PurchaseOrderId = id, PurchaseLines = [new() { PurchaseOrderId = id, PurchaseLineId = _line, PurchaseLineNumber = 1, Sku = "SKU-0", PurchaseLineQty = 10, ReceivedQty = 2, RemainingQty = 8, Unit = "PCS" }], Items = [new() { InboundLineId = Guid.NewGuid(), InboundDocumentNo = "IN-20261020-01", PurchaseLineId = _line, PurchaseLineNumber = 1, LineNumber = 1, Sku = "SKU-0", Qty = 2, Unit = "PCS", PostingStatus = SwiftExport.Contracts.SysEnums.Inventory.InboundPostingStatusEnum.Posted, ReceiptDate = new DateTime(2026, 10, 20), PostedDate = new DateTime(2026, 10, 20, 9, 0, 0), CountsTowardReceipt = true }] })); }
        public void Dispose() => Vm.Dispose();
    }
    /// <summary>严格分派本视角已确认的四个只读接口。</summary>
    public class ApiProxy : DispatchProxy
    {
        public Func<MethodInfo?, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod, args);
    }
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    private sealed class TrackingUser : ICurrentUserService
    {
        public event EventHandler? CurrentUserChanged;
        public MeDto? CurrentUser { get; private set; } = new() { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ProcurementOrderRead, PermissionKeys.ProcurementProgressView, PermissionKeys.InventoryInboundView] };
        public bool IsAuthenticated => CurrentUser is not null;
        public string DisplayName => "跟踪测试用户";
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => CurrentUser?.Permissions ?? [];
        public bool IsAdmin => false;
        public bool HasPermission(string key) => Permissions.Contains(key);
        public void SetCurrentUser(MeDto user) { CurrentUser = user; CurrentUserChanged?.Invoke(this, EventArgs.Empty); }
        public void Clear() { CurrentUser = null; CurrentUserChanged?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class TrackingNavigation : INavigationService
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];
        public NavigationTabItem? SelectedTab { get; set; }
        public List<string> Keys { get; } = [];
        public Task OpenAsync(string routeKey) => Task.CompletedTask;
        public Task OpenDocumentAsync(string routeKey, string documentKey, object? parameter = null, CancellationToken cancellationToken = default) { Keys.Add(documentKey); return Task.CompletedTask; }
        public void UpdateDocumentKey(object contentViewModel, string documentKey) { }
        public void Activate(NavigationTabItem? tab) { }
        public Task CloseAsync(NavigationTabItem? tab) => Task.CompletedTask;
        public void Reset() { }
    }
}
