using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Operations;
using SwiftExport.Wpf.EnterpriseGrid.Operations.Internal;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Operations;

[TestClass]
public sealed class DataGridLineReorderTests
{
    [TestMethod]
    public void EnableDisableAndReload_ShouldOwnExactlyOneConfiguredColumn()
    {
        StaTest.RunAsync(async () =>
        {
            var dataGrid = CreateGrid([new TestRow("A")]);
            var originalColumn = new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(TestRow.Name)) };
            dataGrid.Columns.Add(originalColumn);
            DataGridLineReorder.SetColumnHeader(dataGrid, "排序");
            DataGridLineReorder.SetColumnWidth(dataGrid, new DataGridLength(64));
            DataGridLineReorder.SetColumnDisplayIndex(dataGrid, 0);
            DataGridLineReorder.SetIsEnabled(dataGrid, true);
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);

            var window = Show(dataGrid);
            try
            {
                var reorderColumn = dataGrid.Columns.OfType<LineReorderDataGridColumn>().Single();
                Assert.AreEqual("排序", reorderColumn.Header);
                Assert.AreEqual(64d, reorderColumn.Width.Value);
                Assert.AreEqual(0, reorderColumn.DisplayIndex);
                Assert.IsTrue(reorderColumn.IsReadOnly);
                Assert.IsFalse(reorderColumn.CanUserSort);

                DataGridLineReorder.SetIsEnabled(dataGrid, true);
                Assert.HasCount(1, dataGrid.Columns.OfType<LineReorderDataGridColumn>());

                DataGridLineReorder.SetIsEnabled(dataGrid, false);
                Assert.HasCount(0, dataGrid.Columns.OfType<LineReorderDataGridColumn>());
                Assert.IsTrue(dataGrid.Columns.Contains(originalColumn));

                DataGridLineReorder.SetIsEnabled(dataGrid, true);
                Assert.HasCount(1, dataGrid.Columns.OfType<LineReorderDataGridColumn>());

                window.Content = null;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                Assert.HasCount(0, dataGrid.Columns.OfType<LineReorderDataGridColumn>());
                window.Content = dataGrid;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                dataGrid.UpdateLayout();
                Assert.HasCount(1, dataGrid.Columns.OfType<LineReorderDataGridColumn>());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void Buttons_ShouldForwardRowAndRespectCommandCanExecute()
    {
        StaTest.RunAsync(async () =>
        {
            var rows = new[] { new TestRow("A"), new TestRow("B"), new TestRow("C") };
            var upCommand = new CaptureCommand(parameter => !ReferenceEquals(parameter, rows[0]));
            var downCommand = new CaptureCommand(parameter => !ReferenceEquals(parameter, rows[^1]));
            var dataGrid = CreateGrid(rows);
            DataGridLineReorder.SetMoveUpCommand(dataGrid, upCommand);
            DataGridLineReorder.SetMoveDownCommand(dataGrid, downCommand);
            Enable(dataGrid);

            using var host = new WindowScope(Show(dataGrid));
            dataGrid.UpdateLayout();
            var buttons = FindVisualDescendants<Button>(dataGrid)
                .Where(button => button.ToolTip is "上移一行" or "下移一行")
                .ToArray();
            Assert.HasCount(6, buttons);

            foreach (var row in rows)
            {
                Assert.HasCount(2, buttons.Where(button => ReferenceEquals(button.CommandParameter, row)));
            }

            var firstUp = FindButton(buttons, rows[0], "上移一行");
            var middleUp = FindButton(buttons, rows[1], "上移一行");
            var lastDown = FindButton(buttons, rows[^1], "下移一行");
            Assert.IsFalse(firstUp.IsEnabled);
            Assert.IsTrue(middleUp.IsEnabled);
            Assert.IsFalse(lastDown.IsEnabled);
            Assert.IsFalse(middleUp.Focusable);
            Assert.IsFalse(middleUp.IsTabStop);

            var peer = new ButtonAutomationPeer(middleUp);
            var invokeProvider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            invokeProvider.Invoke();
            await Dispatcher.Yield(DispatcherPriority.Input);
            Assert.AreSame(rows[1], upCommand.LastParameter);
            Assert.IsFalse(dataGrid.Columns.OfType<LineReorderDataGridColumn>().Single().CanUserSort);
        });
    }

    [TestMethod]
    public void ReadOnlyAndTransformedView_ShouldDisableWithoutChangingView()
    {
        StaTest.Run(() =>
        {
            var rows = new[] { new TestRow("B"), new TestRow("A") };
            var dataGrid = CreateGrid(rows);
            DataGridLineReorder.SetMoveUpCommand(dataGrid, new CaptureCommand(_ => true));
            DataGridLineReorder.SetMoveDownCommand(dataGrid, new CaptureCommand(_ => true));
            Enable(dataGrid);

            using var host = new WindowScope(Show(dataGrid));
            var column = dataGrid.Columns.OfType<LineReorderDataGridColumn>().Single();
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
            Assert.IsTrue((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));

            view.SortDescriptions.Add(new SortDescription(nameof(TestRow.Name), ListSortDirection.Ascending));
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
            Assert.HasCount(1, view.SortDescriptions);
            view.SortDescriptions.Clear();
            Assert.IsTrue((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));

            view.Filter = _ => true;
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
            Assert.IsNotNull(view.Filter);
            view.Filter = null;

            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TestRow.Name)));
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
            view.GroupDescriptions.Clear();

            view.CustomSort = new TestRowComparer();
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
            view.CustomSort = null;
            Assert.IsTrue((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));

            dataGrid.CanUserSortColumns = true;
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
            dataGrid.CanUserSortColumns = false;

            dataGrid.IsReadOnly = true;
            Assert.AreEqual(Visibility.Collapsed, column.Visibility);
            DataGridLineReorder.SetShowWhenReadOnly(dataGrid, true);
            Assert.AreEqual(Visibility.Visible, column.Visibility);
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
        });
    }

    [TestMethod]
    public void ItemsSourceAndColumnsReplacement_ShouldNotKeepStaleStateOrLoseHostColumns()
    {
        StaTest.RunAsync(async () =>
        {
            var dataGrid = CreateGrid([new TestRow("A")]);
            DataGridLineReorder.SetMoveUpCommand(dataGrid, new CaptureCommand(_ => true));
            DataGridLineReorder.SetMoveDownCommand(dataGrid, new CaptureCommand(_ => true));
            Enable(dataGrid);

            using var host = new WindowScope(Show(dataGrid));
            var hostColumn = new DataGridTextColumn { Header = "新增宿主列" };
            dataGrid.Columns.Add(hostColumn);
            Assert.HasCount(1, dataGrid.Columns.OfType<LineReorderDataGridColumn>());
            Assert.IsTrue(dataGrid.Columns.Contains(hostColumn));

            dataGrid.Columns.Clear();
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.HasCount(1, dataGrid.Columns.OfType<LineReorderDataGridColumn>());

            var replacement = new[] { new TestRow("C"), new TestRow("D") };
            dataGrid.ItemsSource = replacement;
            var replacementView = CollectionViewSource.GetDefaultView(replacement);
            replacementView.Filter = _ => true;
            Assert.IsFalse((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
            replacementView.Filter = null;
            Assert.IsTrue((bool)dataGrid.GetValue(DataGridLineReorder.CanReorderProperty));
        });
    }

    private static DataGrid CreateGrid(IEnumerable<TestRow> rows) => new()
    {
        Width = 360,
        Height = 180,
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserSortColumns = false,
        ItemsSource = rows,
    };

    private static void Enable(DataGrid dataGrid)
    {
        DataGridLineReorder.SetIsEnabled(dataGrid, true);
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
    }

    private static Window Show(DataGrid dataGrid)
    {
        var window = new Window
        {
            Width = 420,
            Height = 240,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid,
        };
        window.Show();
        dataGrid.UpdateLayout();
        return window;
    }

    private static Button FindButton(IEnumerable<Button> buttons, object parameter, string toolTip) =>
        buttons.Single(button => ReferenceEquals(button.CommandParameter, parameter) && Equals(button.ToolTip, toolTip));

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class CaptureCommand(Func<object?, bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter) => canExecute(parameter);

        public void Execute(object? parameter)
        {
            LastParameter = parameter;
        }

        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class TestRowComparer : IComparer
    {
        public int Compare(object? x, object? y) => string.Compare(
            (x as TestRow)?.Name,
            (y as TestRow)?.Name,
            StringComparison.Ordinal);
    }

    private sealed record TestRow(string Name);
}
