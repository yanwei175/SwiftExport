using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Automation;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>以真实 WPF 绑定验证直达文档显示、输入冻结和双击行命中边界。</summary>
public sealed partial class PurchaseOrderProgressWorkspaceTests
{
    /// <summary>直达订单单号可见，选单区隐藏；确认和未决期间冻结真实输入，不丢失原始非法文本。</summary>
    [TestMethod]
    public void Direct_view_hides_picker_and_freezes_bound_inputs() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.OpenAsync();
        var view = new PurchaseOrderProgressWorkspaceView { DataContext = f.Vm };
        var window = FeedbackWindow(view);
        try
        {
            window.Show(); await FeedbackIdle();
            Assert.IsFalse(((Grid)view.FindName("FeedbackFilters")).IsVisible);
            Assert.IsFalse(FeedbackElement<Grid>(view, "OrdersSection").IsVisible);
            var orderText = FeedbackElement<TextBlock>(view, "CurrentOrderNoText");
            Assert.IsTrue(orderText.IsVisible); Assert.AreEqual(f.Context.OrderNo, orderText.Text);
            var quantity = FeedbackElement<TextBox>(view, "ReadyQtyInput"); var note = FeedbackElement<TextBox>(view, "ReportNoteInput");
            Assert.IsTrue(quantity.IsEnabled);
            quantity.SetCurrentValue(TextBox.TextProperty, "非法数量"); await FeedbackIdle();
            Assert.AreEqual("非法数量", f.Vm.ReportReadyQtyInput); Assert.IsTrue(f.Vm.HasUnsavedChanges);
            quantity.SetCurrentValue(TextBox.TextProperty, "2"); note.SetCurrentValue(TextBox.TextProperty, "未失焦备注"); await FeedbackIdle();
            Assert.AreEqual("未失焦备注", f.Vm.ReportNote);
            var confirm = new TaskCompletionSource<bool>(); f.Dialogs.Confirm = () => confirm.Task;
            var writing = f.Vm.ReportProgressCommand.ExecuteAsync(null); await FeedbackIdle();
            Assert.IsFalse(quantity.IsEnabled); Assert.IsFalse(note.IsEnabled);
            confirm.SetResult(false); await writing; await FeedbackIdle(); Assert.IsTrue(quantity.IsEnabled);
            f.Dialogs.Confirm = () => Task.FromResult(true); f.Report = _ => Failure(ServiceErrorCodes.OutcomeUnknown);
            await f.Vm.ReportProgressCommand.ExecuteAsync(null); await FeedbackIdle();
            Assert.IsFalse(quantity.IsEnabled); Assert.IsFalse(note.IsEnabled);
            Assert.AreEqual("未失焦备注", f.Vm.ReportNote); Assert.IsTrue(f.Vm.RetryReportCommand.CanExecute(null));
        }
        finally { window.Close(); await FeedbackIdle(); }
    });

    /// <summary>空白及列头双击不使用已有选中行；实际采购行双击才展开反馈。</summary>
    [TestMethod]
    public void Only_actual_line_double_click_opens_feedback() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.OpenAsync(); f.Vm.IsDrawerOpen = false;
        var view = new PurchaseOrderProgressWorkspaceView { DataContext = f.Vm }; var window = FeedbackWindow(view);
        try
        {
            window.Show(); await FeedbackIdle(); var grid = FeedbackElement<DataGrid>(view, "LinesGrid");
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = grid });
            Assert.IsFalse(f.Vm.IsDrawerOpen);
            var header = FeedbackDescendants(grid).OfType<DataGridColumnHeader>().First(x => x.Column is not null);
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = header });
            Assert.IsFalse(f.Vm.IsDrawerOpen);
            grid.ScrollIntoView(f.Vm.SelectedLine); grid.UpdateLayout(); await FeedbackIdle();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(f.Vm.SelectedLine);
            Assert.IsNotNull(row);
            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
            await FeedbackIdle(); Assert.IsTrue(f.Vm.IsDrawerOpen);
        }
        finally { window.Close(); await FeedbackIdle(); }
    });

    /// <summary>撤销读取权限清除行数据时，选择绑定回写不得隐式放弃原始非法输入。</summary>
    [TestMethod]
    public void Permission_revocation_keeps_raw_input_when_selection_binding_clears() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.OpenAsync();
        var view = new PurchaseOrderProgressWorkspaceView { DataContext = f.Vm }; var window = FeedbackWindow(view);
        try
        {
            window.Show(); await FeedbackIdle(); var quantity = FeedbackElement<TextBox>(view, "ReadyQtyInput");
            quantity.SetCurrentValue(TextBox.TextProperty, "尚未有效的数量"); await FeedbackIdle();
            f.SetUser("u", view: false, report: false); await FeedbackIdle();
            Assert.HasCount(0, f.Vm.Lines); Assert.AreEqual("尚未有效的数量", f.Vm.ReportReadyQtyInput);
            Assert.IsFalse(quantity.IsEnabled); Assert.IsTrue(f.Vm.HasUnsavedChanges);
        }
        finally { window.Close(); await FeedbackIdle(); }
    });

    private static Window FeedbackWindow(object view) => new()
    { Content = view, Width = 1400, Height = 900, Left = -5000, Top = -5000, ShowInTaskbar = false, ShowActivated = false };
    private static async Task FeedbackIdle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    /// <summary>使用实际可访问性身份定位抽屉的视觉元素，不跨自定义控件 namescope 注册名称。</summary>
    private static T FeedbackElement<T>(DependencyObject root, string id) where T : DependencyObject
        => FeedbackDescendants(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static IEnumerable<DependencyObject> FeedbackDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in FeedbackDescendants(child)) yield return descendant; }
    }
}
