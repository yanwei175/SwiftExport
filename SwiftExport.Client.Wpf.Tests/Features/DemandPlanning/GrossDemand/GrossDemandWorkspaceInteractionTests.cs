using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Views;
using SwiftExportClient.Resources.Controls.Filter;
using DataGridExtensions;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using SwiftExportClient.Resources.Commands;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;
[DoNotParallelize]
public sealed partial class GrossDemandWorkspaceViewModelTests
{
    /// <summary>真实弹窗提交客户多选，更多条件显式应用，本地列筛选不改变已加载数据。</summary>
    [TestMethod]
    public void Workspace_ActualPopupsFiltersAndMenus() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); var first = CreateItem("SO-A", false); first.DemandQty = 1.234567m; first.Unit = "PCS"; first.LinkSku = "NEUTRAL-A";
        var api = new FakeGrossDemandApi { SearchResponse = CreatePage(first, CreateItem("SO-B", false)) };
        api.Customers.AddRange([new() { Id = Guid.NewGuid(), Code = "C1", Name = "客户一" }, new() { Id = Guid.NewGuid(), Code = "C2", Name = "客户二" }]);
        using var vm = CreateViewModel(api); await vm.InitializeAsync(); var view = new GrossDemandWorkspaceView { DataContext = vm }; var window = new Window { Width = 1400, Height = 900, Content = view, ShowInTaskbar = false };
        try
        {
            window.Show(); await GrossIdle(); var grid = GrossDescendants(view).OfType<DataGrid>().Single();
            Assert.AreEqual(60d, grid.RowHeight); Assert.IsTrue(grid.EnableRowVirtualization); Assert.IsTrue(grid.EnableColumnVirtualization); Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit); Assert.IsTrue(grid.GetIsAutoFilterEnabled());
            CollectionAssert.AreEqual(new[] { "来源单号", "图片", "产品编码", "中性型号", "客户型号", "数量", "单位", "要求交期" }, grid.Columns.Take(8).Select(column => (string)column.Header).ToArray());
            Assert.IsFalse(grid.Columns.Any(column => Equals(column.Header, "链路证据"))); Assert.IsFalse(GrossDescendants(grid).OfType<CheckBox>().Any());
            var selector = (MultiSelectLookupButton)view.FindName("CustomerSelector"); GrossToggle((ToggleButton)selector.FindName("PART_Button")); await GrossIdle(); var popup = (Popup)selector.FindName("PART_Popup");
            foreach (var choice in GrossDescendants(popup.Child).OfType<ToggleButton>()) GrossToggle(choice); Assert.AreEqual(0, vm.SelectedCustomers.Count);
            GrossInvoke(GrossDescendants(popup.Child).OfType<Button>().Single(button => Equals(button.Content, "确定"))); await GrossIdle(); await vm.SearchCommand.ExecuteAsync(null);
            CollectionAssert.AreEquivalent(api.Customers.Select(customer => customer.Id).ToArray(), api.LastSearchRequest!.CustomerIds);
            vm.SourceNosText = "SO-A\nSO-B"; vm.IsMultiLineFilterOpen = true; await GrossIdle(); vm.CollapseMoreFiltersCommand.Execute(null); Assert.AreEqual("SO-A\nSO-B", vm.SourceNosText);
            await vm.ApplyMoreFiltersCommand.ExecuteAsync(null); CollectionAssert.AreEqual(new[] { "SO-A", "SO-B" }, api.LastSearchRequest.SourceNos);
            await GrossIdle(); grid.UpdateLayout(); await GrossIdle();
            GrossPreview(view, "gross-workspace.png");
            var texts = GrossDescendants(grid).OfType<TextBlock>().Select(text => text.Text).ToArray(); Assert.IsTrue(texts.Contains(first.DemandQty.ToString("0.############################", CultureInfo.CurrentCulture)), string.Join(" | ", texts)); Assert.IsTrue(texts.Contains("PCS"));
            var column = grid.Columns.Single(column => Equals(column.Header, "来源单号")); var filter = GrossDescendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single();
            GrossToggle((ToggleButton)filter.Template.FindName("FilterButton", filter)); await Task.Delay(250); await GrossIdle();
            var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter); choices.UnselectAll(); choices.SelectedItems.Add("SO-A"); await Task.Delay(350); await GrossIdle(); filter.IsPopupOpen = false;
            Assert.AreEqual(1, grid.Items.Count); Assert.AreEqual(2, vm.Items.Count);
            grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true; await GrossIdle();
            Assert.IsTrue(grid.ContextMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "查看"))); Assert.IsTrue(grid.ContextMenu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "打开跟踪"))); grid.ContextMenu.IsOpen = false;
            GrossPreview(view, "gross-workspace.png");
            DataGridFilterCommands.Clear.Execute(grid); await GrossIdle(); grid.SelectAll(); Assert.AreEqual(2, grid.SelectedItems.Count);
            var selectedRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(grid.Items[1]);
            selectedRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = selectedRow });
            Assert.AreEqual(2, grid.SelectedItems.Count, "右击已选行不能清空用于复制的多选。");
        }
        finally { window.Close(); await GrossIdle(); }
    });

    private static async Task GrossIdle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> GrossDescendants(DependencyObject root)
    { yield return root; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var child in GrossDescendants(VisualTreeHelper.GetChild(root, index))) yield return child; }
    private static void GrossToggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)!).Toggle();
    private static void GrossInvoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void GrossPreview(FrameworkElement view, string name)
    {
        var directory = Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf.Tests", "TestResults", "GrossDemand"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) { var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight); context.DrawRectangle(Brushes.White, null, bounds); context.DrawRectangle(new VisualBrush(view), null, bounds); }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
