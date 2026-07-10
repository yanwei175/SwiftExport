using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
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

    private sealed record TestRow(string Name);
}
