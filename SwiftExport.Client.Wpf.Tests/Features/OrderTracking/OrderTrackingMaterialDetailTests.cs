using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.OrderTracking.MaterialDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.MaterialDetail.Views;
using SwiftExportClient.Features.OrderTracking.DemandDetail.Views;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using SwiftExportClient.Features.OrderTracking.Overview.Views;
using SwiftExportClient.Navigation;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SwiftExport.Client.Wpf.Tests.Features.OrderTracking;

public sealed partial class OrderTrackingTests
{
    /// <summary>关注事项精确授权，逾期只作用来源查询；完整子表与撤权清理保持独立。</summary>
    [TestMethod]
    public void Attention_RequiresDimensionPermissionAndDoesNotFilterChildren() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Overview;
        Assert.IsFalse(vm.AttentionOptions.Any(x => x.Value == OrderTrackingAttentionCodes.Overdue));
        vm.SelectedAttentions.Add(new(OrderTrackingAttentionCodes.Overdue, "逾期"));
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Api.SourceRequests.Count); StringAssert.Contains(vm.ValidationSummary!, "权限");
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ShippingPackingListView] });
        await vm.SearchCommand.ExecuteAsync(null); CollectionAssert.AreEqual(new[] { OrderTrackingAttentionCodes.Overdue }, f.Api.SourceRequests.Single().AttentionCodes); await ChooseSource(f);
        Assert.AreEqual(vm.SelectedSource!.SourceGroupId, f.Api.Requests.Single().SourceGroupId); Assert.AreEqual(2, vm.LoadedDemandCount);
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView] });
        Assert.AreEqual(0, vm.SelectedAttentions.Count); Assert.IsFalse(vm.AttentionOptions.Any(x => x.Value == OrderTrackingAttentionCodes.Overdue)); Assert.AreEqual(0, vm.Items.Count);
    });

    /// <summary>真实关注多选弹窗仅显示有权候选，确定后提交并集，撤权移除隐藏选择。</summary>
    [TestMethod]
    public void Attention_PopupTracksPermissionsAndAppliesTwoCodes() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Overview.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = f.Overview };
        await WithWindow(view, async () =>
        {
            var selector = (MultiSelectLookupButton)view.FindName("AttentionSelector");
            Assert.AreEqual(2, f.Overview.AttentionOptions.Count);
            f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView, PermissionKeys.ShippingPackingListView] }); await Idle();
            Assert.AreEqual(4, f.Overview.AttentionOptions.Count); OpenPopup(selector); await Idle();
            var popup = (Popup)selector.FindName("PART_Popup"); var choices = Descendants(popup.Child).OfType<ToggleButton>().ToArray();
            Assert.AreEqual(4, choices.Length); Toggle(choices[0]); Toggle(choices[1]); Assert.AreEqual(0, f.Overview.SelectedAttentions.Count);
            await Click(Descendants(popup.Child).OfType<Button>().Single(x => Equals(x.Content, "确定"))); await f.Overview.SearchCommand.ExecuteAsync(null);
            Assert.AreEqual(2, f.Api.SourceRequests.Last().AttentionCodes.Count);
            f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [PermissionKeys.OrderTrackingView] }); await Idle();
            Assert.IsFalse(f.Overview.SelectedAttentions.Any(x => x.Value == OrderTrackingAttentionCodes.Overdue));
            Assert.IsTrue(f.Overview.SelectedAttentions.All(x => f.Overview.AttentionOptions.Any(option => option.Value == x.Value)));
        });
    });
    /// <summary>历史物料可按NetDemandId打开，同型号不同身份不合并，返回复用已有成品需求页签。</summary>
    [TestMethod]
    public void MaterialNavigation_ReusesIdentityAndReturnsToDemand() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); var grossId = f.Api.MaterialGrossDemandId; var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        f.Api.Material = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingMaterialDto>>.Ok([new() { NetDemandId = firstId, Sku = "SAME", IsDeleted = true }, new() { NetDemandId = secondId, Sku = "SAME" }]));
        await f.Navigation.OpenDocumentAsync(DemandTrackingDetailViewModel.RouteKey, grossId.ToString("N"), grossId); var grossTab = f.Navigation.SelectedTab!; var demand = (DemandTrackingDetailViewModel)grossTab.ContentViewModel!;
        await demand.Materials.ReloadCommand.ExecuteAsync(null); demand.SelectedMaterial = demand.Materials.Data![0]; Assert.IsTrue(demand.OpenMaterialCommand.CanExecute(null));
        await demand.OpenMaterialCommand.ExecuteAsync(null); var firstTab = f.Navigation.SelectedTab!; await demand.OpenMaterialCommand.ExecuteAsync(null);
        Assert.AreSame(firstTab, f.Navigation.SelectedTab); Assert.AreEqual(2, f.Navigation.OpenTabs.Count); Assert.AreEqual(firstId.ToString("N"), firstTab.DocumentKey);
        demand.SelectedMaterial = demand.Materials.Data[1]; await demand.OpenMaterialCommand.ExecuteAsync(null); var second = (MaterialTrackingDetailViewModel)f.Navigation.SelectedTab!.ContentViewModel!;
        Assert.AreEqual(3, f.Navigation.OpenTabs.Count); Assert.AreEqual(secondId, second.Header.Data!.NetDemandId);
        await second.ReturnToDemandCommand.ExecuteAsync(null); Assert.AreSame(grossTab, f.Navigation.SelectedTab); Assert.AreSame(demand.Materials.Data[1], demand.SelectedMaterial);
        using var blank = f.NewMaterialDetail(); Assert.IsFalse(blank.ReturnToDemandCommand.CanExecute(null));
        f.Api.MaterialHeader = (id, _) => Task.FromResult(ServiceResult<MaterialTrackingDetailDto>.Ok(new() { NetDemandId = id, GrossDemandId = Guid.Empty, Material = new() { NetDemandId = id } }));
        await blank.InitializeDocumentAsync(Guid.NewGuid(), CancellationToken.None); Assert.IsFalse(blank.ReturnToDemandCommand.CanExecute(null));
    });

    /// <summary>物料三个区域分别校验基础、采购及收货权限，缺权限不发请求。</summary>
    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, true, true)]
    public void MaterialPermissions_AreIndependent(bool material, bool purchase, bool receipt) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var permissions = new List<string> { PermissionKeys.OrderTrackingView };
        if (material) permissions.Add(PermissionKeys.DemandPlanningMaterialView); if (purchase) permissions.Add(PermissionKeys.ProcurementOrderRead); if (receipt) permissions.Add(PermissionKeys.InventoryInboundView);
        f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = permissions }); using var vm = f.NewMaterialDetail(); await vm.InitializeDocumentAsync(Guid.NewGuid(), CancellationToken.None);
        await Task.WhenAll(vm.Purchases.EnsureLoadedAsync(), vm.Receipts.EnsureLoadedAsync());
        Assert.AreEqual(material ? 1 : 0, f.Api.SliceCalls.Count(x => x.Area == "material-header"));
        Assert.AreEqual(material && purchase ? 1 : 0, f.Api.SliceCalls.Count(x => x.Area == "material-purchases"));
        Assert.AreEqual(material && purchase && receipt ? 1 : 0, f.Api.SliceCalls.Count(x => x.Area == "material-receipts"));
        var registry = new NavigationRouteRegistry(); var access = new NavigationAccessService(registry, f.User); Assert.AreEqual(material, access.CanAccess(MaterialTrackingDetailViewModel.RouteKey));
    });

    /// <summary>未知承接量不补零；采购失败或收货身份错误不遮挡物料快照和其他区域。</summary>
    [TestMethod]
    public void MaterialSections_KeepUnknownQuantitiesAndFailureIsolation() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewMaterialDetail(); var id = Guid.NewGuid(); await vm.InitializeDocumentAsync(id, CancellationToken.None); var header = vm.Header.Data;
        Assert.IsTrue(header!.Material.IsDeleted); Assert.AreEqual(1, f.Api.SliceCalls.Count);
        f.Api.MaterialPurchase = (_, _) => Task.FromResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Fail("采购暂不可用"));
        f.Api.MaterialReceipt = (_, _) => Task.FromResult(ServiceResult<DemandTrackingReceiptSectionDto>.Ok(new() { NetDemandId = Guid.NewGuid() }));
        await Task.WhenAll(vm.Purchases.ReloadCommand.ExecuteAsync(null), vm.Receipts.ReloadCommand.ExecuteAsync(null));
        Assert.AreEqual("加载失败，可重试", vm.Purchases.StatusText); StringAssert.Contains(vm.Receipts.ErrorMessage!, "物料需求身份不一致"); Assert.AreSame(header, vm.Header.Data);
        f.Api.MaterialPurchase = (key, _) => Task.FromResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Ok(new() { NetDemandId = key, NetQty = 240, Unit = "PCS", AssociatedPurchaseQty = null, RemainingToPurchaseQty = null }));
        await vm.Purchases.ReloadCommand.ExecuteAsync(null); Assert.AreEqual("待核实", vm.Purchases.StatusText); Assert.IsNull(vm.Purchases.Data!.AssociatedPurchaseQty); Assert.IsNull(vm.Purchases.Data.RemainingToPurchaseQty);
        f.Api.MaterialPurchase = (key, _) => Task.FromResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Ok(new() { NetDemandId = key, NetQty = 240, Unit = "PCS", AssociatedPurchaseQty = 250, RemainingToPurchaseQty = -10, ReviewReason = "承接超过需求，待核实" }));
        await vm.Purchases.ReloadCommand.ExecuteAsync(null); Assert.AreEqual(-10m, vm.Purchases.Data!.RemainingToPurchaseQty);
    });

    /// <summary>物料切换拦截旧快照和采购响应，释放后迟到收货失败不再写回。</summary>
    [TestMethod]
    public void MaterialLifecycle_RejectsOldIdentityAndDisposedResults() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewMaterialDetail(); var oldId = Guid.NewGuid(); var newId = Guid.NewGuid();
        var old = new TaskCompletionSource<ServiceResult<MaterialTrackingDetailDto>>(); f.Api.MaterialHeader = (_, _) => old.Task; var first = vm.InitializeDocumentAsync(oldId, CancellationToken.None);
        f.Api.MaterialHeader = (id, _) => Task.FromResult(ServiceResult<MaterialTrackingDetailDto>.Ok(new() { NetDemandId = id, Material = new() { NetDemandId = id, Sku = "new" } }));
        await vm.InitializeDocumentAsync(newId, CancellationToken.None); Assert.IsTrue(f.Api.SliceCalls[0].Token.IsCancellationRequested);
        old.SetResult(ServiceResult<MaterialTrackingDetailDto>.Ok(new() { NetDemandId = oldId, Material = new() { NetDemandId = oldId, Sku = "old" } })); await first; Assert.AreEqual(newId, vm.Header.Data!.NetDemandId);
        var purchase = new TaskCompletionSource<ServiceResult<MaterialTrackingPurchaseSectionDto>>(); f.Api.MaterialPurchase = (_, _) => purchase.Task; var load = vm.Purchases.ReloadCommand.ExecuteAsync(null);
        var third = Guid.NewGuid(); await vm.InitializeDocumentAsync(third, CancellationToken.None);
        f.Api.MaterialPurchase = (id, _) => Task.FromResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Ok(new() { NetDemandId = id, AssociatedPurchaseQty = 2, RemainingToPurchaseQty = 0 })); await vm.Purchases.ReloadCommand.ExecuteAsync(null);
        purchase.SetResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Ok(new() { NetDemandId = newId, AssociatedPurchaseQty = 99 })); await load; Assert.AreEqual(2m, vm.Purchases.Data!.AssociatedPurchaseQty);
        var receipts = new TaskCompletionSource<ServiceResult<DemandTrackingReceiptSectionDto>>(); f.Api.MaterialReceipt = (_, _) => receipts.Task; var receiptLoad = vm.Receipts.ReloadCommand.ExecuteAsync(null);
        vm.Dispose(); Assert.IsTrue(f.Api.SliceCalls.Last().Token.IsCancellationRequested); receipts.SetResult(ServiceResult<DemandTrackingReceiptSectionDto>.Fail("late")); await receiptLoad; Assert.IsNull(vm.Receipts.ErrorMessage); Assert.IsNull(vm.Receipts.Data);
    });
    /// <summary>物料页会话变化取消所有在途区域，迟到响应不能泄露旧数据。</summary>
    [TestMethod]
    public void MaterialSessionChange_ClearsAllRegions() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewMaterialDetail(); var id = Guid.NewGuid(); await vm.InitializeDocumentAsync(id, CancellationToken.None);
        var purchase = new TaskCompletionSource<ServiceResult<MaterialTrackingPurchaseSectionDto>>(); var receipt = new TaskCompletionSource<ServiceResult<DemandTrackingReceiptSectionDto>>();
        f.Api.MaterialPurchase = (_, _) => purchase.Task; f.Api.MaterialReceipt = (_, _) => receipt.Task;
        var first = vm.Purchases.ReloadCommand.ExecuteAsync(null); var second = vm.Receipts.ReloadCommand.ExecuteAsync(null); f.User.SetCurrentUser(new MeDto { UserId = "u", Permissions = [] });
        Assert.IsTrue(f.Api.SliceCalls.TakeLast(2).All(x => x.Token.IsCancellationRequested));
        purchase.SetResult(ServiceResult<MaterialTrackingPurchaseSectionDto>.Ok(new() { NetDemandId = id, AssociatedPurchaseQty = 99 })); receipt.SetResult(ServiceResult<DemandTrackingReceiptSectionDto>.Ok(new() { NetDemandId = id })); await Task.WhenAll(first, second);
        Assert.IsNull(vm.Header.Data); Assert.IsNull(vm.Purchases.Data); Assert.IsNull(vm.Receipts.Data); Assert.IsFalse(vm.ReturnToDemandCommand.CanExecute(null));
    });

    /// <summary>实际历史物料入口、快照、承接量及整行收货展示可以完整走通，不虚构共享份额。</summary>
    [TestMethod]
    public void MaterialViews_OpenHistoricalRowAndShowWholeLineProgress() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); using var demand = f.NewDetail(); var netId = Guid.NewGuid();
        f.Api.Material = (_, _) => Task.FromResult(ServiceResult<List<DemandTrackingMaterialDto>>.Ok([new() { NetDemandId = netId, Sku = "M-1001", IsDeleted = true, DecompositionRevision = 2, NetQty = 240, Unit = "PCS" }]));
        await demand.InitializeDocumentAsync(f.Api.MaterialGrossDemandId, CancellationToken.None); demand.SelectedSectionIndex = 1;
        var sourceView = new DemandTrackingDetailView { DataContext = demand };
        using var output = new StringWriter(); using var listener = new TextWriterTraceListener(output); var trace = PresentationTraceSources.DataBindingSource; var previous = trace.Switch.Level; trace.Switch.Level = SourceLevels.Error; trace.Listeners.Add(listener);
        try
        {
            await WithWindow(sourceView, async () =>
            {
                await Settle(); var grid = (DataGrid)sourceView.FindName("MaterialsGrid"); Assert.AreEqual(1, grid.Items.Count); grid.SelectedItem = grid.Items[0]; await Idle();
                Assert.IsTrue(demand.OpenMaterialCommand.CanExecute(null)); SavePreview(sourceView, "material-entry.png");
                var header = Descendants(grid).OfType<DataGridColumnHeader>().First(); grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = header }); Assert.AreEqual(0, f.Navigation.OpenTabs.Count);
                var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)!; var cell = Descendants(row).OfType<DataGridCell>().First();
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = cell }); await Idle();
                Assert.AreEqual(1, f.Navigation.OpenTabs.Count); var vm = (MaterialTrackingDetailViewModel)f.Navigation.SelectedTab!.ContentViewModel!; Assert.AreEqual(netId, vm.Header.Data!.NetDemandId);
                var view = new MaterialTrackingDetailView { DataContext = vm }; Window.GetWindow(sourceView)!.Content = view; await Settle();
                Assert.IsTrue(Descendants(view).OfType<TextBox>().All(x => x.IsReadOnly)); Assert.IsTrue(Descendants(view).OfType<TextBox>().Any(x => x.Text == "已删除历史"));
                var back = Descendants(view).OfType<Button>().Single(x => Equals(x.Content, "返回成品需求")); Assert.IsTrue(back.IsEnabled); SavePreview(view, "material-detail.png");
                vm.SelectedSectionIndex = 1; await Settle(); var purchases = (DataGrid)view.FindName("PurchasesGrid"); Assert.AreEqual(2, purchases.Items.Count);
                Assert.AreEqual(DataGridSelectionUnit.FullRow, purchases.SelectionUnit); Assert.IsTrue(purchases.IsReadOnly); Assert.AreEqual(240m, vm.Purchases.Data!.AssociatedPurchaseQty);
                SavePreview(view, "material-purchases.png"); var (filter, popup) = await OpenFilter(purchases, "型号"); Assert.IsTrue(popup.IsOpen); filter.IsPopupOpen = false;
                vm.SelectedSectionIndex = 2; await Settle(); var progress = Descendants(view).OfType<DataGrid>().Single(x => x.Name == "ReceiptProgressGrid");
                Assert.AreEqual(1, progress.Items.Count); Assert.AreEqual(DataGridSelectionUnit.FullRow, progress.SelectionUnit); Assert.IsTrue(progress.IsReadOnly);
                Assert.AreEqual(500m, vm.Receipts.Data!.PurchaseLines.Single().ReceivedQty); Assert.AreEqual(500m, vm.Receipts.Data.PurchaseLines.Single().RemainingQty);
                Assert.AreEqual(2, vm.Purchases.Data.Items.Count); Assert.IsTrue(vm.Receipts.Data.PurchaseLines.Single().IsSharedPurchaseLine);
                Assert.IsTrue(Descendants(progress).OfType<TextBlock>().Any(x => x.Text == "500.00")); SavePreview(view, "material-receipts.png");
                f.Api.MaterialReceipt = (id, _) => Task.FromResult(ServiceResult<DemandTrackingReceiptSectionDto>.Ok(new() { NetDemandId = id, GrossDemandId = f.Api.MaterialGrossDemandId, PurchaseLines = [new() { PurchaseLineId = Guid.NewGuid(), PurchaseOrderNo = "PO-UNKNOWN", PurchaseLineQty = 1000, ReceivedQty = null, RemainingQty = null, Unit = "PCS", ReviewReason = "历史证据不完整" }] }));
                await vm.Receipts.ReloadCommand.ExecuteAsync(null); await Idle();
                Assert.IsNull(vm.Receipts.Data!.PurchaseLines.Single().ReceivedQty); Assert.IsNull(vm.Receipts.Data.PurchaseLines.Single().RemainingQty);
                Assert.IsTrue(Descendants(progress).OfType<TextBlock>().Count(x => x.Text == "待核实") >= 2); SavePreview(view, "material-receipts-unknown.png");
            });
            Assert.AreEqual("", output.ToString(), "物料详情及共享区域不能产生数据绑定错误。");
        }
        finally { trace.Listeners.Remove(listener); trace.Switch.Level = previous; }
    });

    /// <summary>交付区域直接展示服务端业务日和逾期结论，未知不转成零天或未逾期。</summary>
    [TestMethod]
    public void DeliveryOverdue_UsesServerEvidenceAndPreservesUnknown() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); using var vm = f.NewDetail(); var id = Guid.NewGuid();
        f.Api.Delivery = (key, _) => Task.FromResult(ServiceResult<DemandTrackingDeliverySectionDto>.Ok(new() { GrossDemandId = key, BusinessDate = new DateTime(2030, 1, 1), IsOverdue = true, OverdueDays = 17, DeliveredQty = 50, RemainingQty = 70, Unit = "PCS", Items = [new() { PackingListNo = "PL-EVIDENCE", LineNumber = 1, Qty = 50, Unit = "PCS", CountsTowardDelivery = true, PostedDate = new DateTime(2029, 12, 31) }] }));
        await vm.InitializeDocumentAsync(id, CancellationToken.None); vm.SelectedSectionIndex = 4; var view = new DemandTrackingDetailView { DataContext = vm };
        await WithWindow(view, async () =>
        {
            await Settle(); Assert.AreEqual("逾期", ((TextBlock)view.FindName("OverdueStatusText")).Text); Assert.AreEqual("17", ((TextBlock)view.FindName("OverdueDaysText")).Text);
            Assert.IsTrue(Descendants(view).OfType<TextBlock>().Any(x => x.Text == "2030-01-01")); SavePreview(view, "overdue.png");
            f.Api.Delivery = (key, _) => Task.FromResult(ServiceResult<DemandTrackingDeliverySectionDto>.Ok(new() { GrossDemandId = key, BusinessDate = new DateTime(2030, 1, 1), IsOverdue = null, OverdueDays = null, DeliveredQty = null, RemainingQty = null, ReviewReason = "证据不完整" }));
            await vm.Deliveries.ReloadCommand.ExecuteAsync(null); await Idle(); Assert.AreEqual("待核实", ((TextBlock)view.FindName("OverdueStatusText")).Text); Assert.AreEqual("待核实", ((TextBlock)view.FindName("OverdueDaysText")).Text);
        });
    });
    /// <summary>错误物料身份不能接纳或返回错误成品需求，导航取消也不能接纳迟到快照。</summary>
    [TestMethod]
    public void MaterialHeader_RejectsWrongIdentityAndNavigationCancellation() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(true); using var vm = f.NewMaterialDetail(); var requested = Guid.NewGuid(); var foreign = Guid.NewGuid();
        f.Api.MaterialHeader = (_, _) => Task.FromResult(ServiceResult<MaterialTrackingDetailDto>.Ok(new() { NetDemandId = foreign, GrossDemandId = Guid.NewGuid(), Material = new() { NetDemandId = foreign } }));
        await vm.InitializeDocumentAsync(requested, CancellationToken.None); Assert.IsNull(vm.Header.Data); Assert.IsFalse(vm.ReturnToDemandCommand.CanExecute(null)); StringAssert.Contains(vm.Header.ErrorMessage!, "物料需求身份不一致");
        var pending = new TaskCompletionSource<ServiceResult<MaterialTrackingDetailDto>>(); f.Api.MaterialHeader = (_, _) => pending.Task;
        using var cancellation = new CancellationTokenSource(); var load = vm.InitializeDocumentAsync(requested, cancellation.Token); cancellation.Cancel(); Assert.IsTrue(f.Api.SliceCalls.Last().Token.IsCancellationRequested);
        pending.SetResult(ServiceResult<MaterialTrackingDetailDto>.Ok(new() { NetDemandId = requested, GrossDemandId = Guid.NewGuid(), Material = new() { NetDemandId = requested } })); await load; Assert.IsNull(vm.Header.Data); Assert.IsFalse(vm.ReturnToDemandCommand.CanExecute(null));
    });    /// <summary>真实表格完整承载301条成品需求，本地筛选仅改变显示数且不存在子表分页器。</summary>
    [TestMethod]
    public void SourceAllDemands_ActualGridKeeps301RowsAndFilteredCount() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(true); var vm = f.Overview;
        f.Api.Groups[0].TotalDemandCount = 301; f.Api.Groups[0].MatchedDemandCount = 1;
        f.Api.Search = (type, id, _) => Task.FromResult(Demands(type, id, Enumerable.Range(0, 301).Select(index =>
        {
            var row = Row(index == 0 ? "needle" : "UNMATCHED-" + index, id);
            row.CustomerCode = index == 0 ? "C1" : "C2";
            row.LifecycleStatus = index == 300 ? SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum.Voided : SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum.Active;
            return row;
        }).ToArray()));
        vm.Keyword = "needle"; vm.SelectedCustomerCodes.Add("C1"); vm.SelectedLifecycleStatuses.Add(vm.LifecycleOptions[0]); vm.SelectedAttentions.Add(vm.AttentionOptions.Single(x => x.Value == OrderTrackingAttentionCodes.Overdue));
        await vm.InitializeAsync(); var view = new OrderTrackingOverviewView { DataContext = vm };
        await WithWindow(view, async () =>
        {
            var source = (DataGrid)view.FindName("SourcesGrid"); var grid = (DataGrid)view.FindName("TrackingGrid"); source.SelectedItem = vm.Sources[0]; await Settle();
            Assert.AreEqual(301, grid.Items.Count); Assert.AreEqual(301, vm.VisibleDemandCount); Assert.AreEqual(301, vm.LoadedDemandCount); Assert.AreEqual(1, Descendants(view).OfType<SwiftExportClient.Resources.Controls.PageNavigator>().Count());
            CollectionAssert.Contains(f.Api.SourceRequests.Last().AttentionCodes, OrderTrackingAttentionCodes.Overdue); SavePreview(view, "source-full-301.png");
            var (filter, _) = await OpenFilter(grid, "产品编码"); var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter);
            Assert.AreEqual(301, choices.Items.Count); choices.UnselectAll(); choices.SelectedItems.Add("UNMATCHED-300"); await Settle(); await Settle(); filter.IsPopupOpen = false;
            Assert.AreEqual(1, grid.Items.Count); Assert.AreEqual(301, vm.Items.Count); Assert.AreEqual(1, vm.VisibleDemandCount); StringAssert.Contains(vm.ResultSummary, "全来源已加载 301"); StringAssert.Contains(vm.ResultSummary, "当前筛选显示 1");
            Assert.AreEqual(SwiftExport.Contracts.SysEnums.GrossDemandLifecycleStatusEnum.Voided, ((OrderTrackingDemandRowModel)grid.Items[0]).LifecycleStatus); SavePreview(view, "source-full-filtered.png");
        });
    });}
