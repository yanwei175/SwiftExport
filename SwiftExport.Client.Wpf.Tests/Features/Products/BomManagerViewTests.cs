using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Views;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>使用真实页面、项目资源及原生 DataGrid 检查编辑提交、Lookup 绑定和布局，运行于 STA。</summary>
[TestClass] [DoNotParallelize]
public sealed class BomManagerViewTests
{
    /// <summary>真实分子编辑阻断非法整数；Lookup 完整回填，原生 Esc/Tab 编辑路由仍有效。</summary>
    [TestMethod]
    public void View_UsesActualResourcesBindingsAndNativeEditing() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        var root = RepositoryRoot(); LoadResources(root);
        var fixture = new BomManagerTests.Fixture(); using var vm = fixture.Create(); await vm.SearchCommand.ExecuteAsync(null);
        var view = new BomManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.Background);
            var master = (DataGrid)view.FindName("ProductGrid"); var grid = (DataGrid)view.FindName("ComponentGrid");
            Assert.IsTrue(master.IsReadOnly); Assert.AreSame(vm.SelectedProduct, master.SelectedItem);
            Assert.AreEqual(12, master.Columns.Count); Assert.AreEqual(8, grid.Columns.Count);
            // 驱动真实分页按钮和页大小选择，防止直接传入整数的测试漏掉控件参数契约。
            vm.TotalCount = 201; await Dispatcher.Yield(DispatcherPriority.Background);
            var pager = FindChild<PageNavigator>(view)!;
            var pageItems = ((StackPanel)pager.Content).Children.OfType<ItemsControl>().Single(control => ReferenceEquals(control.ItemsSource, pager.PageItems));
            pageItems.UpdateLayout();
            var lastPage = (ContentPresenter)pageItems.ItemContainerGenerator.ContainerFromItem(pager.PageItems.Last());
            FindChild<Button>(lastPage)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await vm.PageChangeCommand.ExecutionTask!;
            Assert.AreEqual(11, vm.PageIndex);
            ((ComboBox)pager.FindName("CboPageSize")).SelectedItem = 50;
            await vm.PageChangeCommand.ExecutionTask!;
            Assert.AreEqual(1, vm.PageIndex); Assert.AreEqual(50, vm.PageSize);
            await Dispatcher.Yield(DispatcherPriority.Background);
            var line = vm.Boms.Items.Single(); var number = grid.Columns[5];
            grid.ScrollIntoView(line, number); grid.UpdateLayout(); grid.CurrentCell = new(line, number);
            Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.Background);
            var editor = (TextBox)number.GetCellContent(line); editor.Text = "abc";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(editor)); Assert.IsFalse(vm.CommitPendingEdits!());
            editor.Text = "4"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(vm.CommitPendingEdits!()); Assert.AreEqual(4, line.Numerator);
            grid.CurrentCell = new(line, grid.Columns[1]); Assert.IsTrue(grid.BeginEdit());
            await Dispatcher.Yield(DispatcherPriority.Background);
            var lookup = FindChild<ProductLookupTextBox>(grid)!; Assert.IsNotNull(lookup);
            var candidate = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "LOOKUP", DescriptionZh = "选中描述", Finish = "金色", FeaturesZh = "新配置" };
            vm.ProductCandidates.Add(candidate); lookup.SelectedProduct = candidate;
            lookup.SelectionCommittedCommand!.Execute(lookup.SelectionCommittedCommandParameter);
            Assert.AreEqual(candidate.Uid, line.SubSkuId); Assert.AreEqual("LOOKUP", line.SubSku);
            Assert.AreEqual("金色", line.Finish); Assert.AreEqual("新配置", line.Features);
            Assert.IsTrue(vm.CommitPendingEdits!());
            grid.CurrentCell = new(line, grid.Columns[7]); Assert.IsTrue(grid.BeginEdit());
            var note = (TextBox)grid.Columns[7].GetCellContent(line); note.Focus();
            note.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(note)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            // 原生 Esc 首次取消单元格，第二次取消整行事务。
            grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.IsFalse(((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(line)).IsEditing);
            var splitter = FindChild<GridSplitter>(view)!;
            Assert.IsNotNull(splitter); Assert.AreEqual(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
            var layout = (Grid)splitter.Parent;
            Assert.IsTrue(layout.RowDefinitions[Grid.GetRow(splitter) - 1].Height.IsStar);
            Assert.IsTrue(layout.RowDefinitions[Grid.GetRow(splitter) + 1].Height.IsStar);
            Assert.IsNotNull(FindChild<CachedImage>(master)); Assert.IsNotNull(FindChild<CachedImage>(grid));
            // 保存真实布局渲染用于开发检查，不请求图片地址或访问实际 API。
            var preview = new DrawingVisual();
            using (var drawing = preview.RenderOpen())
            {
                drawing.DrawRectangle((Brush)Application.Current.Resources["UserLayerOnMicaBaseAltFillColorDefaultBrush"], null, new Rect(0, 0, 1400, 850));
                drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, 1400, 850));
            }
            var bitmap = new RenderTargetBitmap(1400, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.Combine(root, "artifacts", "bom-management", "BOM管理页面.png");
            using var output = File.Create(path); encoder.Save(output);
            Assert.IsNotNull(new BomBindingDialogView()); Assert.IsNotNull(new BomSourcePickerView()); Assert.IsNotNull(new BomTreeView());
        }
        finally { window.Close(); }
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.IsNull(vm.CommitPendingEdits, "卸载后必须释放模型持有的控件委托。");
    });

    /// <summary>从当前 App 的实际资源清单加载命名样式，避免测试以假资源掩盖运行时错误。</summary>
    private static void LoadResources(string root)
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        var app = XDocument.Load(Path.Combine(root, "SwiftExport.Client.Wpf", "App.xaml"));
        foreach (var source in app.Descendants().Where(node => node.Name.LocalName == "ResourceDictionary")
                     .Select(node => (string?)node.Attribute("Source")).Where(source => source is not null))
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SwiftExport.Client.Wpf;component/" + source) });
    }
    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return root?.FullName ?? throw new AssertFailedException("找不到仓库根目录。");
    }
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        { var child = VisualTreeHelper.GetChild(parent, index); if (child is T found) return found; if (FindChild<T>(child) is { } nested) return nested; }
        return null;
    }
}
