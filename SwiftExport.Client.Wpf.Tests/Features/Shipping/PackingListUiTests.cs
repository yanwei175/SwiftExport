using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Features.Shipping.PackingLists.ViewModels;
using SwiftExportClient.Features.Shipping.PackingLists.Views;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Converters;
using SwiftExportClient.Services.Api;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证路由注册与真实 XAML 实例化，不启动网络或后端。</summary>
[TestClass, DoNotParallelize]
public sealed class PackingListUiTests
{
    /// <summary>Manager 和 Detail 路由使用出货模块权限，视图模型在 DI 中可发现。</summary>
    [TestMethod]
    public void RoutesAndRegistrationsArePresent()
    {
        var routes = new NavigationRouteRegistry();
        Assert.IsTrue(routes.TryGetRoute(PackingListManagerViewModel.RouteKey, out var manager));
        Assert.IsTrue(routes.TryGetRoute(PackingListDetailViewModel.RouteKey, out var detail));
        Assert.AreEqual("Shipping.PackingList.View", manager.RequiredPermission);
        Assert.AreEqual("Shipping.PackingList.View", detail.RequiredPermission);
        var services = new ServiceCollection(); services.AddSwiftExportClientViews();
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(PackingListManagerViewModel)));
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(PackingListDetailViewModel)));
    }
    /// <summary>真实主题下实例化三张页面；管理表始终只读，详情提交回调可接入。</summary>
    [TestMethod]
    public void ViewsLoadWithRealResourcesAndReadonlyManagerGrids() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        var resources = Application.Current.Resources;
        resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        foreach (var path in new[] { "Resources/UserWpfUiThemeResources.xaml", "Resources/Variables.xaml", "Resources/Colors.xaml", "Resources/Typography.xaml",
            "Resources/Grids.xaml", "Resources/Borders.xaml", "Resources/Button.xaml", "Resources/DatePicker.xaml", "Resources/DataGrid.xaml",
            "Resources/Image.xaml", "Features/Shipping/页面模板.xaml" })
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/{path}") });
        resources["EnumDescriptionConverter"] = new EnumDescriptionConverter();
        resources["InverseBooleanConverter"] = new InverseBooleanConverter();
        var api = new PackingListApiStub(); var ui = new PackingListUiStub();
        var runner = new ApiCallRunner(NullLogger<ApiCallRunner>.Instance);
        using var manager = new PackingListManagerViewModel(api, runner, new PackingListNavigationStub(), ui, ui, new ReferenceDataCacheServiceStub());
        var managerView = new PackingListManagerView { DataContext = manager }; Layout(managerView);
        var grids = Descendants(managerView).OfType<DataGrid>().ToArray();
        Assert.AreEqual(2, grids.Length); Assert.IsTrue(grids.All(x => x.IsReadOnly));
        using var detail = new PackingListDetailViewModel(api, runner, new ReferenceDataCacheServiceStub(), new PackingListSourceStub(),
            new PackingListNavigationStub(), ui, ui);
        await detail.InitializeDocumentAsync(new PackingListOpenRequest { Uid = Guid.NewGuid(), IsNew = true }, default);
        var splitRow = detail.Lines.AddNew(new SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListLineDto
        { Uid = Guid.NewGuid(), PackingListHeadId = detail.Header.Uid, GrossDemandId = Guid.NewGuid(),
            IsNonStandardSnapshot = true, Qty = 105, PcsPerPackage = 20 });
        var detailView = new PackingListDetailView { DataContext = detail }; Layout(detailView);
        detailView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.IsNotNull(detail.CommitEdits); Assert.IsTrue(detail.CommitEdits());
        var splitButton = Descendants(detailView).OfType<Button>().Single(x => ReferenceEquals(x.Command, detail.SplitTailCommand));
        Assert.AreSame(detail.SplitTailCommand, splitButton.Command);
        Assert.AreSame(splitRow, splitButton.CommandParameter);
        Assert.IsTrue(splitButton.IsEnabled);
        splitButton.Command.Execute(splitButton.CommandParameter);
        Assert.AreEqual(2, detail.Lines.Items.Count);
        // 上表是包装条目，下表保留真实成员，拼箱不再重复显示成员产品。
        var allLines = (DataGrid)detailView.FindName("OrderLinesGrid");
        var members = (DataGrid)detailView.FindName("PackingMembersGrid");
        allLines.SelectedItems.Clear();
        foreach (var package in detail.Lines.Packages.ToArray()) allLines.SelectedItems.Add(package);
        await detail.PackProductsCommand.ExecuteAsync(null); Layout(detailView);
        var group = detail.SelectedPackage!;
        Assert.IsTrue(group.IsMixed);
        Assert.AreEqual(2, Descendants(detailView).OfType<DataGrid>().Count());
        Assert.HasCount(0, Descendants(detailView).OfType<Expander>().ToArray());
        Assert.AreEqual(1, allLines.Items.Count); Assert.AreEqual(2, members.Items.Count);
        Assert.AreSame(group, allLines.Items[0]);
        Assert.IsTrue(members.Items.Cast<object>().All(x => detail.Lines.Items.Any(row => ReferenceEquals(row, x))));
        Assert.IsFalse(members.IsReadOnly);
        Assert.IsTrue(SwiftExportClient.Resources.Behaviors.RowDragReorder.GetIsEnabled(allLines));
        Assert.IsTrue(SwiftExportClient.Resources.Behaviors.RowDragReorder.GetIsEnabled(members));
        members.SelectedItem = splitRow; Layout(detailView);
        Assert.AreSame(splitRow, detail.SelectedLine); Assert.AreSame(group, allLines.SelectedItem);
        // 编辑整箱单元格，格式错误阻止提交及切箱，修正后一次回写所有成员。
        var netColumn = allLines.Columns.Single(x => Equals(x.Header, "每箱净重"));
        allLines.CurrentCell = new DataGridCellInfo(group, netColumn);
        allLines.ScrollIntoView(group, netColumn); Layout(detailView);
        Assert.IsTrue(allLines.BeginEdit()); Layout(detailView);
        var groupNet = Descendants(allLines).OfType<TextBox>()
            .Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "拼箱每箱净重");
        groupNet.Text = "bad"; Assert.IsFalse(detail.CommitEdits!());
        // 临时空箱只是客户端容器，切换不能丢弃当前单元格的非法文本。
        var empty = new PackingListPackingGroupModel(Guid.NewGuid(), true);
        detail.Lines.PackingGroups.Add(empty); Layout(detailView);
        allLines.SelectedItem = empty; Layout(detailView);
        Assert.AreSame(group, detail.SelectedPackage); Assert.AreEqual("bad", groupNet.Text);
        groupNet.Text = "3"; Assert.IsTrue(detail.CommitEdits!()); Layout(detailView);
        Assert.IsTrue(detail.Lines.Items.All(x => x.NwPerPackage == 3m));
        allLines.SelectedItem = empty; Layout(detailView);
        Assert.AreSame(empty, detail.SelectedPackingGroup); Assert.AreEqual(0, members.Items.Count);
        await detail.DissolvePackageCommand.ExecuteAsync(null); Layout(detailView);
        Assert.AreEqual(1, allLines.Items.Count);
        allLines.SelectedItem = group; Layout(detailView);
        members.SelectedItem = splitRow; Layout(detailView);
        await detail.MoveOutOfPackingGroupCommand.ExecuteAsync(null); Layout(detailView);
        Assert.AreEqual(2, allLines.Items.Count); Assert.IsNull(splitRow.PackingGroupId);
        Assert.AreEqual(105m, detail.Lines.Items.Sum(x => x.Qty));
        // 右键的动态目标绑定到页面命令，而不是包装模型。
        var host = new Window { Content = detailView, Width = 1600, Height = 900, ShowActivated = false, ShowInTaskbar = false }; host.Show();
        var menu = allLines.ContextMenu!;
        try
        {
        menu.PlacementTarget = allLines; menu.IsOpen = true;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Layout(detailView);
        var addMenu = (MenuItem)menu.Items[1]; addMenu.IsSubmenuOpen = true; menu.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var targetMenu = (MenuItem)addMenu.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.IsNotNull(targetMenu); Assert.AreSame(detail.PackProductsCommand, targetMenu.Command);
        Assert.AreSame(group, targetMenu.CommandParameter);
        }
        finally { ((MenuItem)menu.Items[1]).IsSubmenuOpen = false; menu.IsOpen = false; menu.PlacementTarget = null; host.Content = null; host.Close(); }
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await detail.UndoCommand.ExecuteAsync(null); Layout(detailView);
        Assert.AreEqual(0, members.Items.Count); Assert.IsNull(detail.SelectedPackingGroup);
        detailView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Assert.IsNull(detail.CommitEdits);
        var customer = Guid.NewGuid();
        api.Sources = _ => Task.FromResult(SwiftExport.Contracts.Wrappers.ServiceResult<
            SwiftExport.Contracts.Wrappers.PagedResponseBase<SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListSourceCandidateDto>>.Ok(new()
            { Items = Enumerable.Range(0, 2).Select(_ => new SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListSourceCandidateDto
                { GrossDemandId = Guid.NewGuid(), CustomerId = customer, IsNonStandard = true, DemandVersion = 1, SourceVersion = 1, RemainingQty = 2 }).ToList() }));
        using var picker = new PackingListSourcePickerViewModel(api, runner);
        await picker.InitializeAsync(customer, new Dictionary<Guid, decimal>(), default);
        var pickerView = new PackingListSourcePickerView { DataContext = picker }; Layout(pickerView);
        pickerView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var pickerGrid = Descendants(pickerView).OfType<DataGrid>().Single();
        Assert.IsTrue(pickerGrid.IsReadOnly);
        Assert.AreEqual(DataGridSelectionMode.Extended, pickerGrid.SelectionMode);
        Assert.AreEqual(DataGridSelectionUnit.FullRow, pickerGrid.SelectionUnit);
        pickerGrid.SelectedItems.Add(picker.Items[0]);
        pickerGrid.SelectedItems.Add(picker.Items[1]);
        Assert.IsTrue(picker.Items.All(x => x.IsChecked));
        Assert.AreEqual(2, picker.GetSelectedResult().Count);
        picker.ClearSelectionCommand.Execute(null);
        Assert.AreEqual(0, pickerGrid.SelectedItems.Count);
        Assert.IsTrue(picker.Items.All(x => !x.IsChecked));
        pickerGrid.SelectedItem = picker.Items[1];
        Assert.IsTrue(picker.Items[1].IsChecked);
        Assert.IsFalse(picker.Items[0].IsChecked);
        pickerView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        // 分配模式使用真实单元格模板，数量输入须回写选中行并控制确认门禁。
        using var allocator = new PackingListSourcePickerViewModel(api, runner);
        allocator.ConfigureAllocation(null, true, null, 3);
        await allocator.InitializeAsync(customer, new Dictionary<Guid, decimal>(), default);
        var allocationView = new PackingListSourcePickerView { DataContext = allocator }; Layout(allocationView);
        var allocationGrid = Descendants(allocationView).OfType<DataGrid>().Single();
        allocationGrid.SelectedItem = allocator.Items[0]; Layout(allocationView);
        var quantityEditor = Descendants(allocationGrid).OfType<TextBox>()
            .Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "AllocationQtyText" && ReferenceEquals(x.DataContext, allocator.Items[0]));
        Assert.IsTrue(quantityEditor.IsEnabled);
        Assert.IsFalse(allocator.IsPrimaryButtonEnabled);
        quantityEditor.Text = "1.5"; quantityEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.AreEqual(1.5m, allocator.Items[0].AllocationQty);
        Assert.IsTrue(allocator.IsPrimaryButtonEnabled);
        quantityEditor.Text = "bad"; quantityEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.IsFalse(allocator.IsPrimaryButtonEnabled);

        // 三张新增弹窗使用真实 XAML 和主题，检查实际绑定及只读网格而非仅检查源码字符串。
        var options = new PackingListPostingOptions { IsReverse = true, BusinessDate = new DateTime(2026, 9, 17), Reason = "原始原因" };
        var optionsView = new PackingListPostingOptionsView { DataContext = options }; Layout(optionsView);
        var datePicker = Descendants(optionsView).OfType<DatePicker>().Single();
        Assert.AreEqual(options.BusinessDate, datePicker.SelectedDate);
        datePicker.SelectedDate = new DateTime(2026, 9, 18);
        Assert.AreEqual(new DateTime(2026, 9, 18), options.BusinessDate);
        var reasonEditor = Descendants(optionsView).OfType<TextBox>().Single(x => x.AcceptsReturn);
        reasonEditor.Text = "修改原因";
        Assert.AreEqual("修改原因", options.Reason);

        var action = new PackingListPostingActionDto { ActionKind = "Shipment", Quantity = 5, Sku = "TEST", ImagePath = null };
        var previewView = new PackingListPostingPreviewView { DataContext = new PackingListPostingPreviewDto {
            Actions = [action], Blockers = [new() { Code = "SHORTFALL", Message = "库存不足" }] } }; Layout(previewView);
        var previewGrids = Descendants(previewView).OfType<DataGrid>().ToArray();
        Assert.AreEqual(2, previewGrids.Length); Assert.IsTrue(previewGrids.All(x => x.IsReadOnly));
        Assert.IsTrue(previewGrids.Any(x => x.Items.Cast<object>().Contains(action)));

        var traceLine = new PackingListExecutionLineTraceDto { ActionKind = "Shipment", Qty = 5, DocumentNo = "OUT-TEST", LedgerId = Guid.NewGuid() };
        var traceView = new PackingListTraceView { DataContext = new PackingListTraceDto {
            Executions = [new() { Operation = 1, BusinessDate = DateTime.Today, Lines = [traceLine] }] } }; Layout(traceView);
        var traceGrid = Descendants(traceView).OfType<DataGrid>().Single();
        Assert.IsTrue(traceGrid.IsReadOnly); Assert.AreSame(traceLine, traceGrid.Items[0]);
        // 排空模板延迟布局后再让 STA 夹具清理全局主题资源。
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    });
    /// <summary>已过账箱单可在下表浏览不同拼箱，但不能编辑整箱指标或执行成员调整。</summary>
    [TestMethod]
    public void PostedDetailAllowsGroupBrowsingWithoutEditing() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var id = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var dto = new PackingListDetailDto
        {
            Header = new() { Uid = id, Version = 1, PostingStatus = SwiftExport.Contracts.SysEnums.Shipping.PackingListPostingStatusEnum.Posted },
            Lines = [new() { Uid = Guid.NewGuid(), PackingListHeadId = id, LineNumber = 1, PackingGroupId = first, Qty = 1, PcsPerPackage = 1 },
                     new() { Uid = Guid.NewGuid(), PackingListHeadId = id, LineNumber = 2, PackingGroupId = second, Qty = 2, PcsPerPackage = 2 }]
        };
        var api = new PackingListApiStub { Get = _ => Task.FromResult(SwiftExport.Contracts.Wrappers.ServiceResult<PackingListDetailDto>.Ok(dto)) };
        var ui = new PackingListUiStub();
        using var detail = new PackingListDetailViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), new ReferenceDataCacheServiceStub(),
            new PackingListSourceStub(), new PackingListNavigationStub(), ui, ui);
        await detail.InitializeDocumentAsync(new PackingListOpenRequest { Uid = id }, default);
        var view = new PackingListDetailView { DataContext = detail }; Layout(view);
        var selector = (DataGrid)view.FindName("OrderLinesGrid");
        var members = (DataGrid)view.FindName("PackingMembersGrid");
        Assert.IsTrue(selector.IsEnabled);
        selector.SelectedItem = detail.Lines.PackingGroups[0]; Layout(view);
        Assert.AreSame(detail.Lines.Items[0], members.Items[0]);
        selector.SelectedItem = detail.Lines.PackingGroups[1]; Layout(view);
        Assert.AreSame(detail.Lines.Items[1], members.Items[0]);
        Assert.IsTrue(selector.IsReadOnly); Assert.IsTrue(members.IsReadOnly);
        Assert.IsFalse(detail.CanReorderPackages); Assert.IsFalse(detail.CanReorderMembers);
        Assert.IsFalse(detail.MoveIntoPackingGroupCommand.CanExecute(null));
        Assert.IsFalse(detail.MoveOutOfPackingGroupCommand.CanExecute(null));
        Assert.IsFalse(detail.Lines.HasChanges);
    });

    private static void Layout(FrameworkElement view)
    { view.Measure(new Size(1600, 900)); view.Arrange(new Rect(0, 0, 1600, 900)); view.UpdateLayout(); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
