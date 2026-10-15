using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Behaviors;
using GongDragDrop = GongSolutions.Wpf.DragDrop.DragDrop;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>验证共享拖拽行为的生命周期、视图门禁、来源隔离及键盘编辑边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class RowDragReorderTests
{
    /// <summary>锚点换算只修正一次下移索引；旧快照、相邻原地和非法位置不能执行。</summary>
    [TestMethod]
    public void RequestsResolveAnchorsWithoutLosingIdentity()
    {
        var items = new[] { new Row(1), new Row(2), new Row(3) };
        Assert.IsTrue(new RowReorderRequest(items[0], items[2], RowDropPosition.After).TryResolve<Row>(items, out var row, out var index));
        Assert.AreSame(items[0], row); Assert.AreEqual(2, index);
        Assert.IsTrue(new RowReorderRequest(items[2], items[0], RowDropPosition.Before).TryResolve<Row>(items, out _, out index));
        Assert.AreEqual(0, index);
        foreach (var invalid in new[] {
            new RowReorderRequest(items[0], items[1], RowDropPosition.Before),
            new RowReorderRequest(items[0], items[0], RowDropPosition.After),
            new RowReorderRequest(new Row(1), items[2], RowDropPosition.After),
            new RowReorderRequest(items[0], new Row(3), RowDropPosition.After),
            new RowReorderRequest(items[0], null, RowDropPosition.Before),
            new RowReorderRequest(items[0], items[2], (RowDropPosition)99) }) Assert.IsFalse(invalid.TryResolve<Row>(items, out _, out _));
    }

    /// <summary>变换视图、只读、命令拒绝和更换数据源不产生移动；禁用、卸载重载没有重复接线或遗留列。</summary>
    [TestMethod]
    public void LifecycleAndViewsRejectStaleMoves() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        var rows = new ObservableCollection<Row>(Enumerable.Range(1, 4).Select(i => new Row(i)));
        var command = new MoveCommand(rows); var grid = Grid(rows, command);
        var window = new Window { Content = grid, Width = 500, Height = 350, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); grid.UpdateLayout();
            var source = GongDragDrop.GetDragHandler(grid); var target = GongDragDrop.GetDropHandler(grid);
            var first = rows[0]; var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var handle = Descendants(container).OfType<TextBlock>().Single(text => RowDragReorder.GetIsHandle(text));
            var drag = new DragInfo(grid, handle, MouseButton.Left,
                element => handle.TranslatePoint(new Point(handle.ActualWidth / 2, handle.ActualHeight / 2), (UIElement)element));
            source.StartDrag(drag); Assert.AreSame(first, drag.Data);
            var drop = Drop(grid, drag, first, rows[3]); target.DragOver(drop); Assert.AreEqual(DragDropEffects.Move, drop.Effects);
            command.Enabled = false; target.Drop(drop); Assert.AreSame(first, rows[0]); command.Enabled = true;
            var view = CollectionViewSource.GetDefaultView(rows);
            view.Filter = _ => true; target.Drop(drop); Assert.AreSame(first, rows[0]); Assert.IsFalse(source.CanStartDrag(drag)); view.Filter = null;
            view.SortDescriptions.Add(new SortDescription(nameof(Row.Id), ListSortDirection.Descending));
            target.Drop(drop); Assert.AreSame(first, rows[0]); view.SortDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Row.Id)));
            target.Drop(drop); Assert.AreSame(first, rows[0]);
            RowDragReorder.SetAllowBusinessGrouping(grid, true);
            target.DragOver(drop); Assert.AreEqual(DragDropEffects.None, drop.Effects, "分组视图的组头或缺少真实行容器不能成为落点。");
            target.Drop(drop); Assert.AreSame(first, rows[0]);
            RowDragReorder.SetAllowBusinessGrouping(grid, false); view.GroupDescriptions.Clear();
            ((ListCollectionView)view).CustomSort = new RowComparer();
            Assert.IsFalse(source.CanStartDrag(drag)); ((ListCollectionView)view).CustomSort = null;
            grid.IsReadOnly = true; target.Drop(drop); Assert.AreSame(first, rows[0]); grid.IsReadOnly = false;
            target.Drop(Drop(new DataGrid(), drag, first, rows[3])); Assert.AreSame(first, rows[0]);
            grid.ItemsSource = new ObservableCollection<Row> { new(5), new(6) };
            target.Drop(drop); Assert.AreSame(first, rows[0]); grid.ItemsSource = rows;
            RowDragReorder.SetIsEnabled(grid, false); Assert.IsFalse(GongDragDrop.GetIsDragSource(grid)); Assert.IsFalse(source.CanStartDrag(drag));
            RowDragReorder.SetIsEnabled(grid, true);
            window.Content = null; await Dispatcher.Yield(DispatcherPriority.Loaded); Assert.IsFalse(GongDragDrop.GetIsDragSource(grid));
            window.Content = grid; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            GongDragDrop.GetDropHandler(grid).Drop(drop);
            Assert.AreSame(first, rows.Last()); Assert.AreEqual(1, command.Executions); Assert.AreEqual(2, grid.Columns.Count);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>非编辑 Alt 方向键执行重排；编辑状态和已处理输入保留给原生控件。</summary>
    [TestMethod]
    public void KeyboardReordersButEditingKeepsNativeInput() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        var rows = new ObservableCollection<Row> { new(1), new(2), new(3) };
        var command = new MoveCommand(rows); var grid = Grid(rows, command);
        var window = new Window { Content = grid, Width = 500, Height = 350, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[0]); grid.Focus();
            var first = rows[0]; var device = new AltKeyboard();
            var down = new KeyEventArgs(device, PresentationSource.FromVisual(grid), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            grid.RaiseEvent(down); Assert.IsTrue(down.Handled); Assert.AreSame(first, rows[1]);
            grid.CurrentCell = new DataGridCellInfo(first, grid.Columns[1]); Assert.IsTrue(grid.BeginEdit());
            var edit = new KeyEventArgs(device, PresentationSource.FromVisual(grid), 0, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            grid.RaiseEvent(edit); Assert.IsFalse(edit.Handled); Assert.AreEqual(1, command.Executions); grid.CancelEdit();
            var handled = new KeyEventArgs(device, PresentationSource.FromVisual(grid), 0, Key.Up) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Handled = true };
            grid.RaiseEvent(handled); Assert.AreEqual(1, command.Executions);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>创建不加载业务资源的原生双列表格，只声明手柄和编辑文本。</summary>
    private static DataGrid Grid(ObservableCollection<Row> rows, ICommand command)
    {
        var handle = new FrameworkElementFactory(typeof(TextBlock));
        handle.SetValue(TextBlock.TextProperty, "≡"); handle.SetValue(TextBlock.BackgroundProperty, Brushes.Transparent); handle.SetValue(RowDragReorder.IsHandleProperty, true);
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserSortColumns = false };
        grid.Columns.Add(new DataGridTemplateColumn { IsReadOnly = true, Width = 50, CellTemplate = new DataTemplate { VisualTree = handle } });
        grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(Row.Label)), Width = 180 });
        RowDragReorder.SetMoveCommand(grid, command); RowDragReorder.SetIsEnabled(grid, true); return grid;
    }

    /// <summary>只通过公开契约提供库输入，记录效果而不检查内部字段。</summary>
    private static IDropInfo Drop(DataGrid grid, IDragInfo drag, Row source, Row target)
    {
        var values = new Dictionary<string, object?> { ["VisualTarget"] = grid, ["DragInfo"] = drag, ["Data"] = source,
            ["TargetItem"] = target, ["InsertPosition"] = RelativeInsertPosition.AfterTargetItem };
        var proxy = DispatchProxy.Create<IDropInfo, DropProxy>();
        ((DropProxy)proxy).Call = (name, args) =>
        {
            if (name.StartsWith("set_", StringComparison.Ordinal)) { values[name[4..]] = args[0]; return null; }
            return values.GetValueOrDefault(name[4..]);
        };
        return proxy;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>仅用于表格绑定的测试行。</summary>
    private sealed class Row(int id)
    {
        public int Id { get; } = id;
        public string Label { get; set; } = id.ToString();
    }

    /// <summary>模拟页面集合命令，业务移动不会由共享行为直接执行。</summary>
    private sealed class MoveCommand(ObservableCollection<Row> rows) : ICommand
    {
        public bool Enabled { get; set; } = true;
        public int Executions { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => Enabled && parameter is RowReorderRequest request && request.TryResolve<Row>(rows, out _, out _);
        public void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            ((RowReorderRequest)parameter!).TryResolve<Row>(rows, out var row, out var index); rows.Move(rows.IndexOf(row), index); Executions++;
        }
    }

    /// <summary>只提供事件设备的 Alt 状态，不向操作系统发送按键。</summary>
    private sealed class AltKeyboard() : KeyboardDevice(InputManager.Current)
    {
        protected override KeyStates GetKeyStatesFromSystem(Key key) => key == Key.LeftAlt ? KeyStates.Down : KeyStates.None;
    }

    /// <summary>用于检查 CustomSort 门禁的独立比较器。</summary>
    private sealed class RowComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => ((Row)x!).Id.CompareTo(((Row)y!).Id);
    }

    /// <summary>模拟公开拖放接口的可控代理。</summary>
    public class DropProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }
}
