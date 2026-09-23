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
        Assert.AreEqual(PermissionKeys.ModuleShippingAccess, manager.RequiredPermission);
        Assert.AreEqual(PermissionKeys.ModuleShippingAccess, detail.RequiredPermission);
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
        // 真实分组模板仍仅包含产品行，折叠后不会影响可保存的来源集合。
        detail.AddPackingGroupCommand.Execute(null);
        detail.SelectedLine = detail.Lines.Items.Last();
        await detail.MoveIntoPackingGroupCommand.ExecuteAsync(null);
        Layout(detailView);
        var mixedExpander = Descendants(detailView).OfType<Expander>().Single();
        Assert.IsTrue(mixedExpander.IsExpanded);
        mixedExpander.IsExpanded = false;
        Assert.AreEqual(2, detail.Lines.Items.Count);
        Assert.IsFalse(detail.Lines.PackingGroups.Single().IsExpanded);
        mixedExpander.IsExpanded = true;
        Layout(detailView);
        var groupNet = Descendants(detailView).OfType<TextBox>()
            .Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "拼箱整箱净重");
        groupNet.Text = "2";
        groupNet.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.AreEqual(2m, detail.Lines.Items.Last().NwPerPackage);
        detailView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Assert.IsNull(detail.CommitEdits);
        var customer = Guid.NewGuid();
        api.Sources = _ => Task.FromResult(SwiftExport.Contracts.Wrappers.ServiceResult<
            SwiftExport.Contracts.Wrappers.PagedResponseBase<SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListSourceCandidateDto>>.Ok(new()
            { Items = Enumerable.Range(0, 2).Select(_ => new SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListSourceCandidateDto
                { GrossDemandId = Guid.NewGuid(), CustomerId = customer, IsNonStandard = true, RemainingQty = 2 }).ToList() }));
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
