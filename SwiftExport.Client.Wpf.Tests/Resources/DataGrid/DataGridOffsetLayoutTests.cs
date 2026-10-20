using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;

namespace SwiftExport.Client.Wpf.Tests.Resources;

/// <summary>验证真实采购从表及WPF-UI模板在小数横滚、缩放和宽度变化时的偏移绑定。</summary>
[TestClass, DoNotParallelize]
public sealed class DataGridOffsetLayoutTests
{
    /// <summary>记录实际模板、DPI与最小偏移，保留可审查的运行证据。</summary>
    public TestContext TestContext { get; set; } = null!;
    /// <summary>记录布局舍入前后的模板宽度绑定，不以自制模板替代用户实际触发的模板。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public void ActualTemplate_FractionalScrollingDoesNotAssignNegativeButtonWidth(bool rounding) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var view = new PurchaseOrderManagerView(); var grid = (System.Windows.Controls.DataGrid)view.FindName("LinesGrid");
        grid.UseLayoutRounding = rounding;
        var ordinaryButton = new FrameworkElementFactory(typeof(Button)); ordinaryButton.SetValue(FrameworkElement.WidthProperty, 37d);
        ordinaryButton.SetValue(Button.CommandProperty, System.Windows.Controls.DataGrid.SelectAllCommand);
        grid.Columns.Add(new DataGridTemplateColumn { Header = "普通单元格按钮", Width = 80, CellTemplate = new DataTemplate { VisualTree = ordinaryButton } });
        grid.ItemsSource = Enumerable.Range(1, 20).Select(i => new PurchaseOrderManagementLineModel { LineNumber = i, Sku = "SKU-" + i, Quantity = i, UnitSnapshot = "PCS" }).ToList();
        using var errors = new StringWriter(); using var listener = new TextWriterTraceListener(errors);
        var trace = PresentationTraceSources.DataBindingSource; var previous = trace.Switch.Level; trace.Switch.Level = SourceLevels.Error; trace.Listeners.Add(listener);
        var window = new Window { Content = view, Width = 900, Height = 850, Left = -5000, Top = -5000, ShowInTaskbar = false, ShowActivated = false };
        var minimum = double.MaxValue;
        try
        {
            window.Show(); await Idle(); grid.UpdateLayout();
            var scroll = (ScrollViewer)grid.Template.FindName("DG_ScrollViewer", grid);
            var originalGridTemplate = grid.Template; var originalScrollTemplate = scroll.Template;
            Assert.AreEqual("PassiveScrollViewer", scroll.GetType().Name, "必须使用当前应用真实WPF-UI模板。");
            var corner = Descendants(scroll).OfType<Button>().Single(x => BindingOperations.GetBinding(x, FrameworkElement.WidthProperty)?.Path?.Path == nameof(System.Windows.Controls.DataGrid.CellsPanelHorizontalOffset));
            Assert.AreEqual(DataGridHeadersVisibility.Column, grid.HeadersVisibility);
            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                view.LayoutTransform = new ScaleTransform(scale, scale);
                foreach (var width in new[] { 900.37, 1000.13 })
                {
                    window.Width = width; await Idle(); grid.UpdateLayout();
                    foreach (var offset in new[] { 0.11339633129517068, 10.23, 80.37, 140.61, 300.19, scroll.ScrollableWidth })
                    {
                        scroll.ScrollToHorizontalOffset(offset); await Idle(); grid.UpdateLayout(); await Idle();
                        minimum = Math.Min(minimum, grid.CellsPanelHorizontalOffset);
                        Assert.IsTrue(double.IsFinite(corner.Width) && corner.Width >= 0,
                            $"舍入={rounding}，缩放={scale}，横滚={scroll.HorizontalOffset:R}，面板偏移={grid.CellsPanelHorizontalOffset:R}，按钮宽度={corner.Width:R}。{errors}");
                        Assert.AreEqual(Math.Max(0d, grid.CellsPanelHorizontalOffset), corner.Width, 0.000001);
                    }
                }
            }
            var ordinaryButtons = Descendants(grid).OfType<Button>().Where(x => x.Command == System.Windows.Controls.DataGrid.SelectAllCommand && !ReferenceEquals(x, corner)).ToArray();
            Assert.IsTrue(ordinaryButtons.Length > 0); foreach (var button in ordinaryButtons) Assert.AreEqual(37d, button.Width);
            // 正常行头保留正宽度；页面重载继续适配同一真实模板，不替换模板或冻结角部宽度。
            grid.HeadersVisibility = DataGridHeadersVisibility.All; scroll.ScrollToHorizontalOffset(10.23); await Idle(); grid.UpdateLayout(); await Idle();
            Assert.IsTrue(grid.CellsPanelHorizontalOffset > 0); Assert.AreEqual(grid.CellsPanelHorizontalOffset, corner.Width, 0.000001);
            window.Content = null; await Idle(); window.Content = view; await Idle(); grid.UpdateLayout();
            Assert.AreSame(originalGridTemplate, grid.Template); Assert.AreSame(originalScrollTemplate, scroll.Template);
            Assert.AreEqual(grid.CellsPanelHorizontalOffset, corner.Width, 0.000001);
            // 模拟主题重用原始模板重新创建子树，恢复后再次触发小数横滚，不能保留指向旧按钮的适配。
            grid.Template = null; grid.Template = originalGridTemplate; grid.ApplyTemplate(); await Idle(); grid.UpdateLayout(); await Idle();
            scroll = (ScrollViewer)grid.Template.FindName("DG_ScrollViewer", grid);
            corner = Descendants(scroll).OfType<Button>().Single(x => BindingOperations.GetBinding(x, FrameworkElement.WidthProperty)?.Path?.Path == nameof(System.Windows.Controls.DataGrid.CellsPanelHorizontalOffset));
            grid.HeadersVisibility = DataGridHeadersVisibility.Column; scroll.ScrollToHorizontalOffset(10.23); await Idle(); grid.UpdateLayout(); await Idle();
            Assert.AreEqual(Math.Max(0d, grid.CellsPanelHorizontalOffset), corner.Width, 0.000001);
            TestContext.WriteLine($"WPF-UI {scroll.GetType().Name}；DPI={VisualTreeHelper.GetDpi(grid).DpiScaleX:R}；舍入={rounding}；最小原生偏移={minimum:R}；重建后按钮宽度={corner.Width:R}。");
            listener.Flush(); Assert.AreEqual(string.Empty, errors.ToString(), $"舍入={rounding}，实际最小面板偏移={minimum:R}。");
        }
        finally { window.Close(); trace.Listeners.Remove(listener); trace.Switch.Level = previous; }
    });
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; } }
}
