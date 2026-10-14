using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>在真实 STA 宿主中验证系列本地候选、显式确认及原生表格取消编辑。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductSeriesAutoSuggestComboBoxTests
{
    /// <summary>下拉从宿主候选本地筛选，任意文字不能提交；键盘确认与清空分别传递宿主命令。</summary>
    [TestMethod]
    public void LocalCandidates_ShouldRequireMatchAndForwardConfirmedSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var original = Series("原系列", "FIRST");
            var next = Series("新系列", "NEXT");
            var commit = new RecordingCommand();
            var clear = new RecordingCommand();
            var parameter = new object();
            var lookup = new ProductSeriesAutoSuggestComboBox
            {
                ItemsSource = new[] { original, next }, SelectedSeries = original, Text = original.DataNameZh,
                SelectionCommittedCommand = commit, SelectionCommittedCommandParameter = parameter,
                SelectionClearedCommand = clear, SelectionClearedCommandParameter = parameter
            };
            var window = WindowFor(lookup);
            try
            {
                window.Show(); window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = (LookupTextBox)lookup.FindName("LookupEditor");
                Assert.AreEqual(LookupTextBoxSearchMode.Local, editor.SearchMode);
                Assert.IsTrue(editor.ShowDropDownButton);
                Assert.IsTrue(editor.RequireItemMatch);
                Assert.AreEqual(nameof(SysDictDataDto.Uid), editor.SelectedValuePath);
                Assert.AreSame(original, editor.SelectedItem);
                Assert.IsFalse(editor.IsDropDownOpen);
                editor.SearchDelay = TimeSpan.FromMilliseconds(20);
                lookup.FocusEditor();
                await Dispatcher.Yield(DispatcherPriority.Input);
                var input = (TextBox)editor.FindName("InputTextBox");
                input.Text = "任意不存在的系列";
                await Task.Delay(80);
                Key(input, System.Windows.Input.Key.Enter);
                Assert.AreSame(original, lookup.SelectedSeries);
                Assert.AreEqual(0, commit.Count);
                lookup.FocusEditor();
                // FocusEditor 通过 Input 优先级调度恢复编辑状态，须先完成真实焦点进入再模拟用户输入。
                await Dispatcher.Yield(DispatcherPriority.Input);
                Assert.IsTrue(input.IsKeyboardFocusWithin);
                input.Text = "NEX";
                await Task.Delay(80);
                var results = (ListBox)editor.FindName("ResultList");
                Assert.AreEqual(1, results.Items.Count);
                Assert.AreSame(next, results.Items[0]);
                Assert.AreSame(original, lookup.SelectedSeries, "搜索文字只影响候选，不提前提交系列身份。");
                Assert.IsTrue(editor.IsDropDownOpen);
                results.SelectedItem = next;
                Key(input, System.Windows.Input.Key.Enter);
                Assert.AreSame(next, lookup.SelectedSeries);
                Assert.AreEqual(1, commit.Count);
                Assert.AreSame(parameter, commit.Parameter);
                Assert.IsFalse(editor.IsDropDownOpen);
                ((Button)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsNull(lookup.SelectedSeries);
                Assert.AreEqual(1, clear.Count);
                Assert.AreSame(parameter, clear.Parameter);
                lookup.IsEnabled = false;
                Assert.IsFalse(editor.IsEnabled);
                Assert.IsFalse(input.IsEnabled);
                Assert.AreEqual("新系列", next.DataNameZh, "候选 DTO 不承担宿主草稿状态。");
            }
            finally { window.Close(); }
        });
    }

    /// <summary>原生 DataGrid 进入编辑可聚焦输入，Esc 与取消编辑保留已保存名称和身份且不提交命令。</summary>
    [TestMethod]
    public void NativeDataGridCancel_ShouldKeepBusinessSeries()
    {
        StaTest.RunAsync(async () =>
        {
            var series = Series("原系列", "ORIGINAL");
            var row = new SeriesRow { SeriesId = series.Uid, SeriesName = series.DataNameZh };
            var committed = new RecordingCommand();
#pragma warning disable CS0618
            var display = new FrameworkElementFactory(typeof(TextBlock));
            display.SetBinding(TextBlock.TextProperty, new Binding(nameof(SeriesRow.SeriesName)));
            var edit = new FrameworkElementFactory(typeof(ProductSeriesAutoSuggestComboBox));
            edit.SetBinding(ProductSeriesAutoSuggestComboBox.TextProperty, new Binding(nameof(SeriesRow.SeriesName)) { Mode = BindingMode.OneWay });
            edit.SetValue(ProductSeriesAutoSuggestComboBox.SelectedSeriesProperty, series);
            edit.SetValue(ProductSeriesAutoSuggestComboBox.ItemsSourceProperty, new[] { series });
            edit.SetValue(ProductSeriesAutoSuggestComboBox.SelectionCommittedCommandProperty, committed);
            var column = new DataGridTemplateColumn
            { CellTemplate = new DataTemplate { VisualTree = display }, CellEditingTemplate = new DataTemplate { VisualTree = edit }, Width = 300 };
#pragma warning restore CS0618
            var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            grid.Columns.Add(column);
            var window = WindowFor(grid);
            try
            {
                window.Show(); window.Activate();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                grid.CurrentCell = new DataGridCellInfo(row, column);
                Assert.IsTrue(grid.BeginEdit());
                // 原生编辑模板 Loaded 会异步调度输入焦点；清空队列后才检查键盘交互。
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var lookup = Descendants<ProductSeriesAutoSuggestComboBox>(grid).Single();
                var editor = Descendants<LookupTextBox>(lookup).Single();
                var input = (TextBox)editor.FindName("InputTextBox");
                Assert.IsTrue(input.IsKeyboardFocusWithin);
                input.Text = "未确认的输入";
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.AreEqual(series.Uid, row.SeriesId);
                Assert.AreEqual(series.DataNameZh, row.SeriesName);
                Key(input, System.Windows.Input.Key.Escape);
                Assert.IsTrue(grid.CancelEdit());
                Assert.AreEqual(series.Uid, row.SeriesId);
                Assert.AreEqual(series.DataNameZh, row.SeriesName);
                Assert.AreEqual(0, committed.Count);
            }
            finally { window.Close(); }
        });
    }

    private static SysDictDataDto Series(string name, string code) => new()
    { Uid = Guid.NewGuid(), DataNameZh = name, DataNameEn = code, IsEnabled = true };

    private static Window WindowFor(UIElement content) => new()
    { Content = content, Width = 480, Height = 220, ShowInTaskbar = false, WindowStyle = WindowStyle.None };

    /// <summary>通过真实键盘路由触发确认或取消，不调用控件的内部方法。</summary>
    private static void Key(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    /// <summary>仅承载原生表格可观察的业务身份和展示名称。</summary>
    private sealed class SeriesRow
    {
        public Guid? SeriesId { get; set; }
        public string SeriesName { get; set; } = string.Empty;
    }

    /// <summary>记录控件对宿主命令的明确提交，候选过滤不调用此命令。</summary>
    private sealed class RecordingCommand : ICommand
    {
        public int Count { get; private set; }
        public object? Parameter { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { Count++; Parameter = parameter; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
