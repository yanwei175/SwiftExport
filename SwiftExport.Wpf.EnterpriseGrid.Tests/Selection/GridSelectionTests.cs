using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Selection;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Selection;

[TestClass]
public sealed class GridSelectionTests
{
    [TestMethod]
    public void CurrentRow_ShouldSynchronizeBothDirections()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var dataGrid = CreateGrid(first, second);

            GridSelection.SetCurrentRow(dataGrid, second);
            Assert.AreSame(second, dataGrid.SelectedItem);

            dataGrid.SelectedItem = first;
            DrainDispatcher();
            Assert.AreSame(first, GridSelection.GetCurrentRow(dataGrid));
        });
    }

    [TestMethod]
    public void CurrentCell_ShouldUseItemAndStableColumnKey()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var dataGrid = CreateGrid(first);
            var nameColumn = dataGrid.Columns[0];

            GridSelection.SetCurrentCell(dataGrid, new GridCellReference(first, "name"));

            Assert.AreSame(first, dataGrid.SelectedItem);
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(nameColumn, dataGrid.CurrentCell.Column);
            Assert.AreEqual("name", GridSelection.GetCurrentColumnKey(dataGrid));
            Assert.AreSame(nameColumn, GridSelection.GetCurrentColumn(dataGrid));
        });
    }

    [TestMethod]
    public void NativeCurrentCellState_ShouldInheritToRealizedCell()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var dataGrid = CreateCellGrid(first);
            try
            {
                GridSelection.SetCurrentCell(dataGrid, new GridCellReference(first, "name"));
                DrainDispatcher();
                var cell = CreateRealizedCell(dataGrid, first, dataGrid.Columns[0]);

                var inheritedState = GridSelection.GetNativeCurrentCellState(cell);
                Assert.IsTrue(inheritedState.IsValid);
                Assert.AreSame(first, inheritedState.Item);
                Assert.AreSame(dataGrid.Columns[0], inheritedState.Column);
            }
            finally
            {
                ((Window)dataGrid.Tag).Close();
            }
        });
    }

    [TestMethod]
    public void CurrentRowChange_ShouldMoveExistingCurrentColumnAndRefreshCellReference()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var dataGrid = CreateGrid(first, second);

            GridSelection.SetCurrentCell(dataGrid, new GridCellReference(first, "name"));
            GridSelection.SetCurrentRow(dataGrid, second);

            Assert.AreSame(second, dataGrid.CurrentCell.Item);
            Assert.AreEqual(new GridCellReference(second, "name"), GridSelection.GetCurrentCell(dataGrid));
        });
    }

    [TestMethod]
    public void CurrentColumnKey_ShouldMoveCurrentCellWithoutChangingRow()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var dataGrid = CreateGrid(first);
            var secondColumn = new DataGridTextColumn { Header = "Other" };
            GridColumn.SetKey(secondColumn, "other");
            dataGrid.Columns.Add(secondColumn);

            GridSelection.SetCurrentCell(dataGrid, new GridCellReference(first, "name"));
            GridSelection.SetCurrentColumnKey(dataGrid, "other");

            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(secondColumn, dataGrid.CurrentCell.Column);
            Assert.AreEqual("other", GridSelection.GetCurrentColumnKey(dataGrid));
        });
    }

    [TestMethod]
    public void DuplicateColumnKey_ShouldFailWithClearDiagnostic()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var dataGrid = CreateGrid(first);
            var duplicate = new DataGridTextColumn { Header = "Duplicate" };
            GridColumn.SetKey(duplicate, "name");
            dataGrid.Columns.Add(duplicate);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                GridSelection.SetCurrentCell(dataGrid, new GridCellReference(first, "name")));

            StringAssert.Contains(exception.Message, "name");
        });
    }

    [TestMethod]
    public void UnknownCurrentRow_ShouldKeepNativeSelectionAsTruth()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var dataGrid = CreateGrid(first);
            dataGrid.SelectedItem = first;

            GridSelection.SetCurrentRow(dataGrid, new TestRow("outside"));

            Assert.AreSame(first, dataGrid.SelectedItem);
            Assert.AreSame(first, GridSelection.GetCurrentRow(dataGrid));
        });
    }

    [TestMethod]
    public void CellChildClick_ShouldSynchronizeSelectionAndCurrentCell()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var dataGrid = CreateGrid(first, second);
            var actionColumn = new DataGridTemplateColumn
            {
                Header = "Action",
                CellTemplate = (DataTemplate)XamlReader.Parse(
                    """
                    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                        <Button Content="Action" />
                    </DataTemplate>
                    """)
            };
            GridColumn.SetKey(actionColumn, "action");
            dataGrid.Columns.Add(actionColumn);

            var window = new Window
            {
                Width = 480,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = dataGrid
            };

            try
            {
                window.Show();
                dataGrid.UpdateLayout();
                dataGrid.ScrollIntoView(first, actionColumn);
                dataGrid.UpdateLayout();

                var firstRow = (DataGridRow?)dataGrid.ItemContainerGenerator.ContainerFromItem(first);
                Assert.IsNotNull(firstRow);
                var button = FindVisualChild<Button>(firstRow);
                Assert.IsNotNull(button);

                dataGrid.SelectedItem = second;
                var coordinator = EnterpriseGridCore.GetCoordinator(dataGrid);
                Assert.IsNotNull(coordinator);

                coordinator.Selection.SynchronizeCellClick(button);

                Assert.AreSame(first, dataGrid.SelectedItem);
                Assert.AreSame(first, GridSelection.GetCurrentRow(dataGrid));
                Assert.AreSame(actionColumn, dataGrid.CurrentCell.Column);
                Assert.AreEqual("action", GridSelection.GetCurrentColumnKey(dataGrid));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void ExtendedCellOrRowHeader_Click_ShouldUpdateCurrentRowWithoutForcingFullRowSelection()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { first, second },
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
            };
            var column = new DataGridTextColumn { Binding = new System.Windows.Data.Binding(nameof(TestRow.Name)) };
            GridColumn.SetKey(column, "name");
            dataGrid.Columns.Add(column);
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            GridSelection.SetIsCellClickSelectionEnabled(dataGrid, true);

            var window = new Window
            {
                Width = 480,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = dataGrid
            };
            try
            {
                window.Show();
                dataGrid.UpdateLayout();
                dataGrid.SelectedCells.Add(new DataGridCellInfo(first, column));
                var selectedBefore = dataGrid.SelectedCells.ToArray();
                dataGrid.ScrollIntoView(second, column);
                dataGrid.UpdateLayout();
                var secondRow = (DataGridRow?)dataGrid.ItemContainerGenerator.ContainerFromItem(second);
                Assert.IsNotNull(secondRow);
                var cell = FindVisualChild<DataGridCell>(secondRow);
                Assert.IsNotNull(cell);

                EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(cell);

                Assert.AreSame(second, GridSelection.GetCurrentRow(dataGrid));
                Assert.AreSame(second, dataGrid.CurrentCell.Item);
                CollectionAssert.AreEquivalent(selectedBefore, dataGrid.SelectedCells.ToArray());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void CellOrRowHeader_InitialCurrentRow_ShouldCreateCurrentCellWithoutSelectingFullRow()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { first },
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
            };
            var column = new DataGridTextColumn { Binding = new System.Windows.Data.Binding(nameof(TestRow.Name)) };
            GridColumn.SetKey(column, "name");
            dataGrid.Columns.Add(column);
            GridSelection.SetCurrentRow(dataGrid, first);
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.AreSame(first, GridSelection.GetCurrentRow(dataGrid));
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.IsNull(dataGrid.SelectedItem);
        });
    }

    [TestMethod]
    public void CurrentCellStateConverter_ShouldMatchItemAndColumnWithoutCachingContainer()
    {
        var first = new TestRow("A");
        var second = new TestRow("B");
        var nameColumn = new DataGridTextColumn();
        var otherColumn = new DataGridTextColumn();
        var converter = new GridCurrentCellStateConverter();
        var current = new DataGridCellInfo(first, nameColumn);

        Assert.AreEqual(
            true,
            converter.Convert(
                [first, nameColumn, current],
                typeof(bool),
                parameter: null!,
                CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.Convert(
                [second, nameColumn, current],
                typeof(bool),
                parameter: null!,
                CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.Convert(
                [first, otherColumn, current],
                typeof(bool),
                parameter: null!,
                CultureInfo.CurrentCulture));
    }

    [TestMethod]
    public void CurrentRowStateConverter_ShouldMatchCurrentCellItemWithoutCachingContainer()
    {
        var first = new TestRow("A");
        var second = new TestRow("B");
        var column = new DataGridTextColumn();
        var converter = new GridCurrentRowStateConverter();
        var current = new DataGridCellInfo(second, column);

        Assert.AreEqual(
            false,
            converter.Convert([first, current], typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            true,
            converter.Convert([second, current], typeof(bool), null!, CultureInfo.CurrentCulture));
    }

    [TestMethod]
    public void SelectionUnitConverter_ShouldDefaultToFullRowAndRoundTripCellModes()
    {
        var converter = new GridSelectionUnitConverter();

        Assert.AreEqual(
            DataGridSelectionUnit.FullRow,
            converter.Convert(false, typeof(DataGridSelectionUnit), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            DataGridSelectionUnit.FullRow,
            converter.Convert(null!, typeof(DataGridSelectionUnit), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            DataGridSelectionUnit.CellOrRowHeader,
            converter.Convert(true, typeof(DataGridSelectionUnit), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            true,
            converter.ConvertBack(DataGridSelectionUnit.Cell, typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            true,
            converter.ConvertBack(DataGridSelectionUnit.CellOrRowHeader, typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.ConvertBack(DataGridSelectionUnit.FullRow, typeof(bool), null!, CultureInfo.CurrentCulture));
        Assert.AreEqual(
            false,
            converter.ConvertBack("invalid", typeof(bool), null!, CultureInfo.CurrentCulture));
    }

    [TestMethod]
    public void CellClick_ShouldWriteTwoWayBindingSourceAndRefreshGeneratedCommand()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var viewModel = new SelectionBindingViewModel();
            var dataGrid = CreateCellGrid(first, second);
            try
            {
                BindingOperations.SetBinding(
                    dataGrid,
                    GridSelection.CurrentRowProperty,
                    new Binding(nameof(SelectionBindingViewModel.SelectedLine))
                    {
                        Source = viewModel,
                        Mode = BindingMode.TwoWay
                    });

                var canExecuteChanged = 0;
                viewModel.DeleteSelectedCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
                EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(
                    CreateRealizedCell(dataGrid, second, dataGrid.Columns[0]));

                Assert.AreSame(second, viewModel.SelectedLine);
                Assert.IsTrue(viewModel.DeleteSelectedCommand.CanExecute(null));
                Assert.IsTrue(canExecuteChanged > 0);
            }
            finally
            {
                ((Window)dataGrid.Tag).Close();
            }
        });
    }

    [TestMethod]
    public void ExtendedCells_SnapshotShouldKeepActiveRowSeparateFromTopSelectedRow()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var third = new TestRow("C");
            var viewModel = new SelectionBindingViewModel();
            var dataGrid = CreateCellGrid(first, second, third);
            try
            {
                BindingOperations.SetBinding(
                    dataGrid,
                    GridSelection.SelectionSnapshotProperty,
                    new Binding(nameof(SelectionBindingViewModel.SelectionSnapshot))
                    {
                        Source = viewModel,
                        Mode = BindingMode.OneWayToSource
                    });

                var column = dataGrid.Columns[0];
                dataGrid.SelectedCells.Add(new DataGridCellInfo(first, column));
                dataGrid.SelectedCells.Add(new DataGridCellInfo(third, column));
                dataGrid.CurrentCell = new DataGridCellInfo(third, column);
                EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(
                    CreateRealizedCell(dataGrid, third, column));

                var snapshot = viewModel.SelectionSnapshot;
                Assert.IsNotNull(snapshot);
                Assert.AreSame(third, snapshot.CurrentRow);
                CollectionAssert.AreEqual(new object[] { first, third }, snapshot.SelectedRows.ToArray());
                Assert.AreSame(first, snapshot.SelectedRows[0]);
                Assert.AreEqual(2, snapshot.SelectedCellCount);
                Assert.IsNull(dataGrid.SelectedItem, "Cell 模式不能通过强写 SelectedItem 伪造当前行。");
            }
            finally
            {
                ((Window)dataGrid.Tag).Close();
            }
        });
    }

    [TestMethod]
    public void CellMode_TransientInvalidCurrentCell_ShouldNotPublishNullCurrentRow()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var second = new TestRow("B");
            var viewModel = new SelectionBindingViewModel();
            var dataGrid = CreateCellGrid(first, second);
            try
            {
                BindCurrentRow(dataGrid, viewModel);
                var column = dataGrid.Columns[0];
                EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(
                    CreateRealizedCell(dataGrid, second, column));
                viewModel.SelectedLineChanges.Clear();

                // 模拟原生 DataGrid 在同一轮 Cell 输入中发布的短暂无效状态。
                dataGrid.CurrentCell = new DataGridCellInfo();
                dataGrid.CurrentCell = new DataGridCellInfo(second, column);
                DrainDispatcher();

                Assert.AreSame(second, viewModel.SelectedLine);
                Assert.IsFalse(viewModel.SelectedLineChanges.Any(value => value is null));
                Assert.IsTrue(viewModel.DeleteSelectedCommand.CanExecute(null));
            }
            finally
            {
                ((Window)dataGrid.Tag).Close();
            }
        });
    }

    [TestMethod]
    public void CellMode_FinalInvalidCurrentCell_ShouldClearCurrentRowAfterCoalescedSync()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var viewModel = new SelectionBindingViewModel();
            var dataGrid = CreateCellGrid(first);
            try
            {
                BindCurrentRow(dataGrid, viewModel);
                EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(
                    CreateRealizedCell(dataGrid, first, dataGrid.Columns[0]));

                dataGrid.CurrentCell = new DataGridCellInfo();
                DrainDispatcher();

                Assert.IsNull(viewModel.SelectedLine);
                Assert.IsFalse(viewModel.DeleteSelectedCommand.CanExecute(null));
            }
            finally
            {
                ((Window)dataGrid.Tag).Close();
            }
        });
    }

    [TestMethod]
    public void CellMode_ItemsReset_ShouldClearCurrentRowAfterCoalescedSync()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A");
            var items = new ObservableCollection<TestRow> { first };
            var viewModel = new SelectionBindingViewModel();
            var dataGrid = CreateCellGrid(first);
            dataGrid.ItemsSource = items;
            try
            {
                BindCurrentRow(dataGrid, viewModel);
                EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(
                    CreateRealizedCell(dataGrid, first, dataGrid.Columns[0]));

                items.Clear();
                DrainDispatcher();

                Assert.IsNull(viewModel.SelectedLine);
                Assert.IsNull(GridSelection.GetCurrentCell(dataGrid));
            }
            finally
            {
                ((Window)dataGrid.Tag).Close();
            }
        });
    }

    private static DataGrid CreateGrid(params TestRow[] rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow
        };

        var nameColumn = new DataGridTextColumn
        {
            Header = "Name",
            Binding = new System.Windows.Data.Binding(nameof(TestRow.Name))
        };
        GridColumn.SetKey(nameColumn, "name");
        dataGrid.Columns.Add(nameColumn);

        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        GridSelection.SetIsCellClickSelectionEnabled(dataGrid, true);
        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return dataGrid;
    }

    private static DataGrid CreateCellGrid(params TestRow[] rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
        };
        var column = new DataGridTextColumn { Binding = new Binding(nameof(TestRow.Name)) };
        GridColumn.SetKey(column, "name");
        dataGrid.Columns.Add(column);
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        GridSelection.SetIsCellClickSelectionEnabled(dataGrid, true);

        var window = new Window
        {
            Width = 320,
            Height = 180,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid
        };
        window.Show();
        dataGrid.UpdateLayout();
        dataGrid.Tag = window;
        return dataGrid;
    }

    private static void BindCurrentRow(DataGrid dataGrid, SelectionBindingViewModel viewModel)
    {
        BindingOperations.SetBinding(
            dataGrid,
            GridSelection.CurrentRowProperty,
            new Binding(nameof(SelectionBindingViewModel.SelectedLine))
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay
            });
    }

    private static DataGridCell CreateRealizedCell(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
        var row = (DataGridRow?)dataGrid.ItemContainerGenerator.ContainerFromItem(item);
        Assert.IsNotNull(row);
        var cells = FindVisualChildren<DataGridCell>(row).ToArray();
        var cell = cells.Single(candidate => ReferenceEquals(candidate.Column, column));
        return cell;
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record TestRow(string Name);
}

internal sealed partial class SelectionBindingViewModel : ObservableObject
{
    public List<object?> SelectedLineChanges { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private object? selectedLine;

    [ObservableProperty]
    private GridSelectionSnapshot? selectionSnapshot;

    partial void OnSelectedLineChanged(object? value)
    {
        SelectedLineChanges.Add(value);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteSelected()
    {
    }

    private bool CanDeleteSelected() => SelectedLine is not null;
}
