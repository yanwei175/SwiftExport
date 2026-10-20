using DataGridExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Resources.Commands;
using SwiftExportClient.Resources.Controls.Filter;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

[DoNotParallelize]
public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>窄窗口的管理从表默认只显示常用事实，查看菜单可恢复备注并滚动到最右列。</summary>
    [TestMethod]
    public void Manager_NarrowLinesCanScrollAndRestoreSecondaryColumns() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var f = CreateFixture(Order("PO-NARROW")); using var vm = f.ViewModel;
        f.Manager.LinesHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok([
            new() { PurchaseOrderLineId = Guid.NewGuid(), Sku = "SKU-NARROW", CustomerSku = "C-SKU", DescriptionZh = "窄窗口滚动验证", Quantity = 10, UnitSnapshot = "PCS", SubNote = "已从查看菜单恢复最右备注列" }]));
        await vm.InitializeAsync(); var view = new PurchaseOrderManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 900, Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); await PurchaseIdle();
            var lines = (DataGrid)view.FindName("LinesGrid");
            CollectionAssert.AreEqual(new[] { "行", "图片", "客户型号", "物料编号", "供应商型号", "中文描述", "数量", "单位", "关联状态", "核实提示", "计划交期", "来源" },
                lines.Columns.Where(x => x.Visibility == Visibility.Visible).Select(x => x.Header!.ToString()).ToArray());
            await DataGridHorizontalAccessAssertions.ReachRightmostColumnAsync(lines, view.ActualWidth);
            await DataGridHorizontalAccessAssertions.RestoreColumnFromMenuAsync(lines, "行备注");
            await DataGridHorizontalAccessAssertions.ReachRightmostColumnAsync(lines, view.ActualWidth);
            DataGridHorizontalAccessAssertions.SaveScreenshot(view, "purchase-manager-narrow-right.png");
        }
        finally { window.Close(); await PurchaseIdle(); }
    });

    /// <summary>真实状态多选、更多条件锚定、主从筛选、可见勾选与原生多选菜单共同工作。</summary>
    [TestMethod]
    public void Manager_ActualMultiSelectPopupsAndMasterDetailFilters() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var fixture = CreateFixture(Order("PO-A"), Order("PO-B")); var vm = fixture.ViewModel;
        fixture.Manager.LinesHandler = (_, _) => Task.FromResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok([
            new() { PurchaseOrderLineId = Guid.NewGuid(), Sku = "A", Quantity = 1.234567m, UnitSnapshot = "PCS" },
            new() { PurchaseOrderLineId = Guid.NewGuid(), Sku = "B", Quantity = 2m, UnitSnapshot = "PCS" }]));
        await vm.InitializeAsync(); using var held = vm; var view = new PurchaseOrderManagerView { DataContext = vm };
        var window = new Window { Width = 1500, Height = 1000, Content = view, ShowInTaskbar = false };
        try
        {
            window.Show(); await PurchaseIdle(); var master = (DataGrid)view.FindName("OrdersGrid"); var lines = (DataGrid)view.FindName("LinesGrid");
            Assert.AreEqual(DataGridSelectionUnit.FullRow, master.SelectionUnit); Assert.AreEqual(DataGridSelectionUnit.FullRow, lines.SelectionUnit);
            Assert.IsTrue(master.EnableRowVirtualization); Assert.IsTrue(lines.EnableColumnVirtualization); Assert.IsTrue(master.GetIsAutoFilterEnabled()); Assert.IsTrue(lines.GetIsAutoFilterEnabled());
            var selectors = PurchaseDescendants(view).OfType<MultiSelectLookupButton>().ToArray(); Assert.AreEqual(4, selectors.Length);
            foreach (var grid in new[] { master, lines })
                Assert.IsFalse(grid.Columns.Any(x => new[] { "货好状态", "收货状态", "收货进度", "预计完成", "已收货", "未收货" }.Contains(x.Header?.ToString())));
            vm.SelectedConfirmationStatuses.Clear(); var status = selectors.Single(x => x.Header == "确认状态");
            PurchaseToggle((ToggleButton)status.FindName("PART_Button")); await PurchaseIdle(); var choicePopup = (Popup)status.FindName("PART_Popup");
            foreach (var option in PurchaseDescendants(choicePopup.Child).OfType<ToggleButton>()) PurchaseToggle(option);
            PurchaseInvoke(PurchaseDescendants(choicePopup.Child).OfType<Button>().Single(x => Equals(x.Content, "确定"))); await PurchaseIdle(); Assert.IsTrue(vm.SelectedConfirmationStatuses.Count > 1);
            var options = (ToggleButton)view.FindName("OptionsToggleButton"); PurchaseToggle(options); await PurchaseIdle();
            var filterPanel = ((FrameworkElement)options.Parent).Parent;
            var more = LogicalTreeHelper.GetChildren(filterPanel).OfType<Popup>().Single();
            Assert.IsInstanceOfType<StackPanel>(more.PlacementTarget); Assert.AreEqual(((FrameworkElement)more.PlacementTarget).ActualWidth, more.Width, 1d);
            vm.OrderDateFrom = new(2026, 1, 1); vm.CollapseMoreFiltersCommand.Execute(null); Assert.AreEqual(new DateTime(2026, 1, 1), vm.OrderDateFrom);
            await vm.ApplyMoreFiltersCommand.ExecuteAsync(null); await PurchaseIdle();
            await FilterPurchaseColumn(master, "采购单号", "PO-A"); Assert.AreEqual(1, master.Items.Count); Assert.AreEqual(2, vm.Orders.Count);
            vm.IsAllCurrentPageChecked = true; Assert.IsTrue(vm.Orders[0].IsChecked); Assert.IsFalse(vm.Orders[1].IsChecked);
            await FilterPurchaseColumn(lines, "物料编号", "A"); Assert.AreEqual(1, lines.Items.Count); Assert.AreEqual(2, vm.Lines.Count);
            lines.ScrollIntoView(lines.Items[0], lines.Columns.Single(x => Equals(x.Header, "数量"))); await PurchaseIdle(); lines.UpdateLayout(); await PurchaseIdle();
            Assert.IsTrue(PurchaseDescendants(lines).OfType<TextBlock>().Any(x => x.Text == 1.234567m.ToString("0.####", CultureInfo.CurrentCulture)));
            foreach (var grid in new[] { master, lines })
            {
                grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true; await PurchaseIdle();
                Assert.IsTrue(grid.ContextMenu.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "查看"))); grid.ContextMenu.IsOpen = false;
            }
            DataGridFilterCommands.Clear.Execute(master); await PurchaseIdle(); master.SelectAll();
            var row = (DataGridRow)master.ItemContainerGenerator.ContainerFromItem(master.Items[1]); row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = row }); Assert.AreEqual(2, master.SelectedItems.Count);
            // 主表选区变化可能重载从表；等待绑定和布局完成后再采图，避免把中间绘制帧当成交付截图。
            await PurchaseIdle(); lines.UpdateLayout(); await PurchaseIdle();
            Assert.AreEqual(2, vm.Lines.Count); Assert.AreEqual(1, lines.Items.Count);
            var directory = Path.Combine(RootForPurchase(), "SwiftExport.Client.Wpf.Tests", "TestResults", "PurchaseOrders"); Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) { var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight); context.DrawRectangle(Brushes.White, null, bounds); context.DrawRectangle(new VisualBrush(view), null, bounds); }
            bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(directory, "purchase-manager.png")); encoder.Save(output);
        }
        finally { window.Close(); await PurchaseIdle(); }
    });

    private static async Task FilterPurchaseColumn(DataGrid grid, string header, string value)
    {
        var column = grid.Columns.Single(x => Equals(x.Header, header)); grid.ScrollIntoView(grid.Items[0], column); await PurchaseIdle(); grid.UpdateLayout(); await PurchaseIdle();
        var filter = PurchaseDescendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single(); PurchaseToggle((ToggleButton)filter.Template.FindName("FilterButton", filter)); await Task.Delay(250); await PurchaseIdle();
        var list = (ListBox)filter.Template.FindName("PART_ListBox", filter); list.UnselectAll(); list.SelectedItems.Add(value); await PurchaseIdle(); filter.IsPopupOpen = false;
        await Task.Delay(grid.GetFilterEvaluationDelay() + TimeSpan.FromMilliseconds(100)); await PurchaseIdle();
    }
    private static async Task PurchaseIdle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> PurchaseDescendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in PurchaseDescendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void PurchaseToggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)!).Toggle();
    private static void PurchaseInvoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static string RootForPurchase()
    { for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName; throw new InvalidOperationException("找不到仓库根目录。"); }
}
