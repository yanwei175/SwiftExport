using SwiftExportClient.Resources.Behaviors;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Resources.GridRegression;

[TestClass]
public sealed class NativeGridEditingCompatibilityTests
{
    [TestMethod]
    public void FullRowExtended_RoutedClicksAcrossTextCells_ShouldOnlyMoveCurrentCell()
    {
        StaTest.Run(() =>
        {
            var row = new TestRow { Name = "A", Code = "001" };
            var probe = new SelectionBindingProbe();
            var dataGrid = CreateGrid(DataGridSelectionUnit.FullRow, row);
            BindSelection(dataGrid, probe);
            var beginningEditCount = 0;
            dataGrid.BeginningEdit += (_, _) => beginningEditCount++;

            using var host = Show(dataGrid);
            var nameCell = FindCell(dataGrid, row, dataGrid.Columns[0]);
            var codeCell = FindCell(dataGrid, row, dataGrid.Columns[1]);

            RaiseMouseClick(nameCell);
            Assert.AreSame(row, dataGrid.SelectedItem);
            Assert.AreSame(nameCell.Column, dataGrid.CurrentCell.Column);
            Assert.IsFalse(nameCell.IsEditing, "首次点击只应选择原生文本 Cell。");

            RaiseMouseClick(codeCell);

            Assert.AreSame(row, dataGrid.SelectedItem);
            Assert.AreSame(codeCell.Column, dataGrid.CurrentCell.Column);
            Assert.AreEqual(0, beginningEditCount, "跨 Cell 单击不得由客户端适配间接触发编辑。");
            Assert.IsFalse(codeCell.IsEditing);
            Assert.AreSame(row, probe.CurrentRow);
            Assert.AreSame(dataGrid.Columns[1], probe.CurrentCell.Column);
        });
    }

    [TestMethod]
    public void CellOrRowHeaderExtended_RoutedClickAfterCommit_ShouldNotBeginEditingTargetCell()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow { Name = "A", Code = "001" };
            var second = new TestRow { Name = "B", Code = "002" };
            var probe = new SelectionBindingProbe();
            var dataGrid = CreateGrid(DataGridSelectionUnit.CellOrRowHeader, first, second);
            BindSelection(dataGrid, probe);
            var beginningEditCount = 0;
            dataGrid.BeginningEdit += (_, _) => beginningEditCount++;

            using var host = Show(dataGrid);
            var firstNameCell = FindCell(dataGrid, first, dataGrid.Columns[0]);
            var secondCodeCell = FindCell(dataGrid, second, dataGrid.Columns[1]);

            RaiseMouseClick(firstNameCell);
            RaiseMouseClick(secondCodeCell);
            Assert.AreSame(second, dataGrid.CurrentCell.Item);
            Assert.AreSame(secondCodeCell.Column, dataGrid.CurrentCell.Column);
            Assert.AreEqual(0, beginningEditCount);
            Assert.IsFalse(secondCodeCell.IsEditing);

            RaiseMouseClick(firstNameCell);
            Assert.IsTrue(dataGrid.BeginEdit(), "原生显式编辑入口应保持可用。");
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            beginningEditCount = 0;

            RaiseMouseClick(secondCodeCell);

            Assert.AreSame(second, dataGrid.CurrentCell.Item);
            Assert.AreSame(secondCodeCell.Column, dataGrid.CurrentCell.Column);
            Assert.AreEqual(0, beginningEditCount, "提交后点击另一 Cell 只能切换 CurrentCell。");
            Assert.IsFalse(secondCodeCell.IsEditing);
            Assert.AreSame(second, probe.CurrentRow);
            Assert.AreSame(dataGrid.Columns[1], probe.CurrentCell.Column);
        });
    }

    [TestMethod]
    public void NativeF2DoubleClickAndTextInputEntry_ShouldRemainAvailable()
    {
        StaTest.Run(() =>
        {
            var row = new TestRow { Name = "A", Code = "001" };
            var dataGrid = CreateGrid(DataGridSelectionUnit.CellOrRowHeader, row);
            var beginningEditCount = 0;
            dataGrid.BeginningEdit += (_, _) => beginningEditCount++;

            using var host = Show(dataGrid);
            var nameCell = FindCell(dataGrid, row, dataGrid.Columns[0]);
            RaiseMouseClick(nameCell);
            nameCell.Focus();

            RaiseKeyDown(nameCell, Key.F2);
            Assert.IsTrue(nameCell.IsEditing, "F2 应继续进入原生编辑状态。");
            Assert.AreEqual(1, beginningEditCount);
            Assert.IsTrue(dataGrid.CancelEdit(DataGridEditingUnit.Cell));

            beginningEditCount = 0;
            RaiseMouseClick(nameCell, clickCount: 2);
            Assert.IsTrue(nameCell.IsEditing, "原生双击编辑入口不得被 Selection 拦截。");
            Assert.AreEqual(1, beginningEditCount);
            Assert.IsTrue(dataGrid.CancelEdit(DataGridEditingUnit.Cell));

            beginningEditCount = 0;
            nameCell.Focus();
            RaiseTextInput(nameCell, "X");
            Assert.IsTrue(nameCell.IsEditing, "文本键盘输入应继续进入原生编辑状态。");
            Assert.AreEqual(1, beginningEditCount);
        });
    }

    private static DataGrid CreateGrid(DataGridSelectionUnit selectionUnit, params TestRow[] rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = selectionUnit
        };
        AddTextColumn(dataGrid, "name", nameof(TestRow.Name));
        AddTextColumn(dataGrid, "code", nameof(TestRow.Code));

        return dataGrid;
    }

    private static void AddTextColumn(DataGrid dataGrid, string key, string path)
    {
        var column = new DataGridTextColumn { Binding = new Binding(path) };

        dataGrid.Columns.Add(column);
    }

    private static void BindSelection(DataGrid dataGrid, SelectionBindingProbe probe)
    {
        BindingOperations.SetBinding(dataGrid, DataGrid.CurrentItemProperty, new Binding(nameof(SelectionBindingProbe.CurrentRow))
        {
            Source = probe,
            Mode = BindingMode.TwoWay
        });
        BindingOperations.SetBinding(dataGrid, DataGrid.CurrentCellProperty, new Binding(nameof(SelectionBindingProbe.CurrentCell))
        {
            Source = probe,
            Mode = BindingMode.TwoWay
        });
    }

    private static IDisposable Show(DataGrid dataGrid)
    {
        var window = new Window
        {
            Width = 480,
            Height = 240,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid
        };
        window.Show();
        dataGrid.UpdateLayout();
        return new WindowScope(window);
    }

    private static DataGridCell FindCell(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
        return FindCellCore(dataGrid, item, column)
            ?? throw new AssertFailedException("未能实现目标 DataGridCell。");
    }

    private static DataGridCell? FindCellCore(DependencyObject parent, object item, DataGridColumn column)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is DataGridCell cell && ReferenceEquals(cell.DataContext, item) && ReferenceEquals(cell.Column, column))
            {
                return cell;
            }

            if (FindCellCore(child, item, column) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static void RaiseMouseClick(DataGridCell cell, int clickCount = 1)
    {
        var args = CreateMouseButtonArgs(cell, UIElement.PreviewMouseLeftButtonDownEvent, clickCount);
        cell.RaiseEvent(args);
        args.RoutedEvent = UIElement.MouseLeftButtonDownEvent;
        cell.RaiseEvent(args);
        DrainDispatcher();
    }

    private static MouseButtonEventArgs CreateMouseButtonArgs(
        DataGridCell cell,
        RoutedEvent routedEvent,
        int clickCount)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = routedEvent,
            Source = cell
        };
        typeof(MouseButtonEventArgs)
            .GetProperty(nameof(MouseButtonEventArgs.ClickCount), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(args, clickCount);
        return args;
    }

    private static void RaiseKeyDown(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new AssertFailedException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Source = target
        });
        DrainDispatcher();
    }

    private static void RaiseTextInput(UIElement target, string text)
    {
        target.RaiseEvent(new TextCompositionEventArgs(
            Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, target, text))
        {
            RoutedEvent = TextCompositionManager.TextInputEvent,
            Source = target
        });
        DrainDispatcher();
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class SelectionBindingProbe
    {
        public object? CurrentRow { get; set; }

        public DataGridCellInfo CurrentCell { get; set; }
    }

    private sealed class TestRow
    {
        public string Name { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;
    }
}
