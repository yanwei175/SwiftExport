using DataGridExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Views;
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

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[DoNotParallelize]
public sealed partial class NetDemandManagerSourceNavigationTests
{
    /// <summary>真实来源多选弹窗、更多条件、列筛选、可见行全选和右键菜单共同工作，保留原生编辑及虚拟化。</summary>
    [TestMethod]
    public void Manager_ActualFiltersMenusAndVisibleSelection() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var api = new RecordingNetDemandApi(Guid.NewGuid()); api.SearchHandler = (request, _) =>
        {
            var page = Page(request, "SKU", 2); page.Data.Items[0].SourceNo = "SO-A"; page.Data.Items[1].SourceNo = "SO-B";
            return Task.FromResult(page);
        };
        using var vm = CreateViewModel(api); await vm.InitializeAsync();
        var view = new NetDemandManagerView { DataContext = vm }; var window = new Window { Width = 1400, Height = 900, Content = view, ShowInTaskbar = false };
        try
        {
            window.Show(); await MaterialIdle(); var grid = (DataGrid)view.FindName("MaterialGrid");
            Assert.AreEqual(60d, grid.RowHeight); Assert.IsTrue(grid.EnableRowVirtualization); Assert.IsTrue(grid.EnableColumnVirtualization);
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit); Assert.IsTrue(grid.GetIsAutoFilterEnabled());
            var source = MaterialDescendants(view).OfType<MultiSelectLookupButton>().Last();
            MaterialToggle((ToggleButton)source.FindName("PART_Button")); await MaterialIdle();
            var lookupPopup = (Popup)source.FindName("PART_Popup");
            foreach (var option in MaterialDescendants(lookupPopup.Child).OfType<ToggleButton>().Take(2)) MaterialToggle(option);
            MaterialInvoke(MaterialDescendants(lookupPopup.Child).OfType<Button>().Single(button => Equals(button.Content, "确定"))); await MaterialIdle();
            Assert.AreEqual(2, vm.SelectedSourceTypes.Count);
            vm.SourceNosText = "SO-A\nSO-B"; MaterialToggle((ToggleButton)view.FindName("OptionsToggle")); await MaterialIdle();
            vm.CollapseMoreFiltersCommand.Execute(null); Assert.AreEqual("SO-A\nSO-B", vm.SourceNosText);
            await vm.ApplyMoreFiltersCommand.ExecuteAsync(null); CollectionAssert.AreEqual(new[] { "SO-A", "SO-B" }, api.SearchRequests.Last().SourceNos);
            await MaterialIdle(); grid.UpdateLayout(); await MaterialIdle();
            var text = MaterialDescendants(grid).OfType<TextBlock>().Select(x => x.Text).ToArray();
            // 数量列位于横向后部，先滚入视图，再核对完整精度和单位。
            grid.ScrollIntoView(vm.Items[0], grid.Columns.Single(x => Equals(x.Header, "物料需求"))); await MaterialIdle(); grid.UpdateLayout();
            text = MaterialDescendants(grid).OfType<TextBlock>().Select(x => x.Text).ToArray();
            Assert.IsTrue(text.Contains(1.234567m.ToString("0.############################", CultureInfo.CurrentCulture)), string.Join(" | ", text));
            Assert.IsTrue(grid.Columns.Any(x => Equals(x.Header, "单位")));
            var column = grid.Columns.Single(x => Equals(x.Header, "来源单号")); grid.ScrollIntoView(vm.Items[0], column); await MaterialIdle(); grid.UpdateLayout();
            var filter = MaterialDescendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single();
            MaterialToggle((ToggleButton)filter.Template.FindName("FilterButton", filter)); await Task.Delay(250); await MaterialIdle();
            var list = (ListBox)filter.Template.FindName("PART_ListBox", filter); list.UnselectAll(); list.SelectedItems.Add("SO-A");
            await MaterialIdle(); filter.IsPopupOpen = false;
            // 筛选库在关闭浮层后按宿主延迟排队应用，断言必须等待真实配置及 Dispatcher 完成。
            await Task.Delay(grid.GetFilterEvaluationDelay() + TimeSpan.FromMilliseconds(100)); await MaterialIdle();
            Assert.AreEqual(1, grid.Items.Count, $"候选={string.Join('|', list.Items.Cast<object>())}; 已选={string.Join('|', list.SelectedItems.Cast<object>())}; 列路径={column.SortMemberPath}; 当前项={string.Join('|', grid.Items.Cast<SwiftExportClient.Features.DemandPlanning.NetDemand.Models.MaterialDemandManagerRowModel>().Select(x => x.SourceNo))}"); Assert.AreEqual(2, vm.Items.Count);
            vm.IsAllChecked = true; Assert.IsTrue(vm.Items[0].IsChecked); Assert.IsFalse(vm.Items[1].IsChecked);
            grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true; await MaterialIdle();
            Assert.IsTrue(grid.ContextMenu.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "查看")));
            Assert.IsTrue(grid.ContextMenu.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "打开物料跟踪"))); grid.ContextMenu.IsOpen = false;
            DataGridFilterCommands.Clear.Execute(grid); await MaterialIdle(); grid.SelectAll(); Assert.AreEqual(2, grid.SelectedItems.Count);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(grid.Items[1]);
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = row });
            Assert.AreEqual(2, grid.SelectedItems.Count);
            var directory = Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf.Tests", "TestResults", "NetDemand"); Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
                context.DrawRectangle(Brushes.White, null, bounds); context.DrawRectangle(new VisualBrush(view), null, bounds);
            }
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(directory, "material-workspace.png")); encoder.Save(output);
        }
        finally { window.Close(); await MaterialIdle(); }
    });

    private static async Task MaterialIdle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> MaterialDescendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in MaterialDescendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void MaterialToggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)!).Toggle();
    private static void MaterialInvoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
}
