using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DataGridExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

/// <summary>用真实布局验证采购表格在窄窗口中可横向滚动，且隐藏列能从既有菜单恢复。</summary>
internal static class DataGridHorizontalAccessAssertions
{
    /// <summary>验证有限视口、可见滚动条及最右列进入视口，不用静态XAML属性替代运行时证据。</summary>
    internal static async Task ReachRightmostColumnAsync(DataGrid grid, double hostWidth)
    {
        await Idle(); grid.UpdateLayout();
        Assert.IsTrue(grid.ActualWidth > 0 && grid.ActualWidth <= hostWidth, "表格不得按全部列宽撑开页面。");
        Assert.AreEqual(ScrollBarVisibility.Auto, grid.HorizontalScrollBarVisibility);
        var scroll = Descendants(grid).OfType<ScrollViewer>().First();
        Assert.IsTrue(scroll.ViewportWidth > 0 && double.IsFinite(scroll.ViewportWidth));
        Assert.IsTrue(scroll.ScrollableWidth > 0, $"{grid.Name}在窄窗口必须保留超宽列的原生滚动范围。");
        Assert.AreEqual(Visibility.Visible, scroll.ComputedHorizontalScrollBarVisibility);
        Assert.IsTrue(Descendants(scroll).OfType<ScrollBar>().Any(x => x.Orientation == Orientation.Horizontal && x.IsVisible));
        scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); await Idle(); grid.UpdateLayout(); await Idle();
        Assert.IsTrue(scroll.HorizontalOffset > 0);
        var last = grid.Columns.Where(x => x.Visibility == Visibility.Visible).MaxBy(x => x.DisplayIndex)!;
        var cell = Descendants(grid).OfType<DataGridCell>().FirstOrDefault(x => x.Column == last && x.IsVisible && x.ActualWidth > 0);
        Assert.IsNotNull(cell, $"最右列“{last.Header}”必须能实际生成并显示。");
        var bounds = cell.TransformToAncestor(grid).TransformBounds(new Rect(cell.RenderSize));
        Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= grid.ActualWidth + 1, $"最右列必须位于可见视口内：{bounds}。");
        Assert.IsTrue(grid.IsReadOnly); Assert.IsTrue(grid.EnableColumnVirtualization); Assert.IsTrue(grid.GetIsAutoFilterEnabled());
    }

    /// <summary>实际打开查看子菜单并通过Automation恢复隐藏列，验证双向显隐绑定不丢列或筛选配置。</summary>
    internal static async Task RestoreColumnFromMenuAsync(DataGrid grid, string header)
    {
        var column = grid.Columns.Single(x => Equals(x.Header, header)); Assert.AreEqual(Visibility.Collapsed, column.Visibility);
        var menu = grid.ContextMenu; menu.PlacementTarget = grid; menu.IsOpen = true; await Idle();
        var columns = menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "查看"));
        Assert.IsTrue(columns.Items.Contains(column)); columns.IsSubmenuOpen = true; await Idle();
        var item = columns.ItemContainerGenerator.ContainerFromItem(column) as MenuItem; Assert.IsNotNull(item);
        var peer = new MenuItemAutomationPeer(item);
        // MenuItem的Toggle只改变勾选状态；用户点击路径还必须触发Click，由既有行为提交显隐绑定。
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        await Idle(); Assert.AreEqual(Visibility.Visible, column.Visibility); Assert.IsTrue(item.IsChecked);
        columns.IsSubmenuOpen = false; menu.IsOpen = false; await Idle();
        Assert.AreSame(column, grid.Columns.Single(x => Equals(x.Header, header)));
    }

    /// <summary>保存带浅色宿主背景的实际窄窗口截图，供统一交付时复核滚动位置和右侧列。</summary>
    internal static void SaveScreenshot(FrameworkElement view, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root); var folder = Path.Combine(root.FullName, "artifacts", "ui"); Directory.CreateDirectory(folder);
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen())
        { context.DrawRectangle(Brushes.White, null, new Rect(view.RenderSize)); context.DrawRectangle(new VisualBrush(view), null, new Rect(view.RenderSize)); }
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
}
