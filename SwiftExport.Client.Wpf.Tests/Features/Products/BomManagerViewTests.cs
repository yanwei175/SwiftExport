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
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Services.Api;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>使用真实页面、项目资源及原生 DataGrid 检查编辑提交、Lookup 绑定和布局，运行于 STA。</summary>
[TestClass] [DoNotParallelize]
public sealed class BomManagerViewTests
{
    /// <summary>真实来源表保持只读、选择复制与统一行高列样式，两处 BOM 主表使用现有查看菜单。</summary>
    [TestMethod]
    public void SourcePicker_ReadOnlyFilterAndColumnMenusUseActualViews() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var fixture = new BomManagerTests.Fixture(); using var manager = fixture.Create(); await manager.SearchCommand.ExecuteAsync(null);
        var managerView = new BomManagerView { DataContext = manager };
        BomProductSearchRequest? sent = null;
        var api = BomManagerTests.Proxy<IBomApi>((_, args) =>
        {
            sent = (BomProductSearchRequest)args![0]!;
            return Task.FromResult(ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(new()
            { TotalCount = 37, Items = [new() { Uid = Guid.NewGuid(), Sku = "SOURCE", BomNo = "BOM", IsBound = true, ComponentCount = 2 }] }));
        });
        using var source = new BomSourcePickerViewModel(api, new ApiCallRunner(NullLogger<ApiCallRunner>.Instance), Guid.NewGuid());
        await source.LoadAsync();
        var sourceView = new BomSourcePickerView { DataContext = source };
        var window = new Window { Content = managerView, Width = 1400, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var managerGrid = (DataGrid)managerView.FindName("ProductGrid");
            Assert.AreEqual("查看", ((MenuItem)managerGrid.ContextMenu.Items[0]).Header);
            var height = managerGrid.RowHeight;
            window.Content = sourceView; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = FindChild<DataGrid>(sourceView)!; Assert.IsTrue(grid.IsReadOnly); Assert.AreEqual(height, grid.RowHeight);
            Assert.IsFalse(grid.BeginEdit()); Assert.IsTrue(source.IsPrimaryButtonEnabled); Assert.AreEqual(37, source.TotalCount);
            Assert.AreSame(grid.FindResource("SwiftExportDataGridWarpTextBlock"), ((DataGridTextColumn)grid.Columns[1]).ElementStyle);
            Assert.AreSame(grid.FindResource("SwiftExportDataGridNumberTextBlock"), ((DataGridTextColumn)grid.Columns[4]).ElementStyle);
            var checkbox = FindChild<CheckBox>(sourceView)!; Assert.IsTrue(checkbox.IsChecked);
            ((IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)!).Toggle();
            await source.SearchCommand.ExecuteAsync(null); Assert.IsFalse(sent!.HasComponents);
            var menu = grid.ContextMenu; Assert.AreEqual("查看", ((MenuItem)menu.Items[0]).Header);
            menu.PlacementTarget = grid; menu.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.Background);
            var columns = (MenuItem)menu.Items[0]; Assert.IsTrue(columns.Items.Count > 0); menu.IsOpen = false;
        }
        finally { window.Close(); }
    });

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
