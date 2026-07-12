using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Operations;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Operations;

[TestClass]
public sealed class GridOperationsTests
{
    [TestMethod]
    public void FillDown_Rectangle_ShouldCopyEachTopValueAndUndoAtomically()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("A", "1"),
                new("B", "2"),
                new("C", "3")
            };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 2, 0, 1);
            var selectedBefore = dataGrid.SelectedCells.ToArray();

            var operations = GetOperations(dataGrid);
            Assert.IsTrue(operations.CanFillDown());
            Assert.IsTrue(operations.FillDown());
            Assert.AreEqual("A", rows[1].Name);
            Assert.AreEqual("A", rows[2].Name);
            Assert.AreEqual("1", rows[1].Code);
            Assert.AreEqual("1", rows[2].Code);
            CollectionAssert.AreEquivalent(selectedBefore, dataGrid.SelectedCells.ToArray());
            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));

            Assert.IsTrue(operations.Undo());
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual("C", rows[2].Name);
            Assert.AreEqual("2", rows[1].Code);
            Assert.AreEqual("3", rows[2].Code);
            CollectionAssert.AreEquivalent(selectedBefore, dataGrid.SelectedCells.ToArray());
            Assert.IsFalse(GridOperations.GetCanUndo(dataGrid));
        });
    }

    [TestMethod]
    public void FillDown_ShouldRejectNonRectangularSelection()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("A", "1"),
                new("B", "2"),
                new("C", "3")
            };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.SelectedCells.Clear();
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[0], dataGrid.Columns[0]));
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[2], dataGrid.Columns[0]));

            Assert.IsFalse(GetOperations(dataGrid).CanFillDown());
            Assert.IsFalse(GetOperations(dataGrid).FillDown());
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual("C", rows[2].Name);
        });
    }

    [TestMethod]
    public void FillDown_ShouldSkipReadOnlyColumnAndClearHistoryOnCollectionChange()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("A", "1"),
                new("B", "2")
            };
            var dataGrid = CreateGrid(rows);
            dataGrid.Columns[1].IsReadOnly = true;
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 1, 0, 1);

            Assert.IsTrue(GetOperations(dataGrid).FillDown());
            Assert.AreEqual("A", rows[1].Name);
            Assert.AreEqual("2", rows[1].Code);
            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));

            rows.Add(new TestRow("D", "4"));
            Assert.IsFalse(GridOperations.GetCanUndo(dataGrid));
        });
    }

    [TestMethod]
    public void FillDown_FullRowSelectionUnit_ShouldBeDisabledWithoutChangingHistory()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("A", "1"),
                new("B", "2")
            };
            var dataGrid = CreateGrid(rows);
            dataGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            using var host = Show(dataGrid);
            dataGrid.SelectedItems.Add(rows[0]);
            dataGrid.SelectedItems.Add(rows[1]);

            var operations = GetOperations(dataGrid);
            Assert.IsFalse(operations.CanFillDown());
            Assert.IsFalse(operations.FillDown());
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual("2", rows[1].Code);
            Assert.IsFalse(GridOperations.GetCanUndo(dataGrid));
        });
    }

    [TestMethod]
    public void Undo_FullRowSelectionUnit_ShouldRemainAvailableForCommittedCell()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1") };
            var dataGrid = CreateGrid(rows);
            dataGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            dataGrid.ScrollIntoView(rows[0], dataGrid.Columns[0]);
            dataGrid.UpdateLayout();

            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = dataGrid.Columns[0].GetCellContent(rows[0]) as TextBox;
            Assert.IsNotNull(editor);
            editor.Text = "Changed";
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            dataGrid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));
            Assert.IsTrue(GetOperations(dataGrid).Undo());
            Assert.AreEqual("A", rows[0].Name);
        });
    }

    [TestMethod]
    public void CommittedCellEdit_ShouldCreateSingleUndoEntry()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1") };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            dataGrid.ScrollIntoView(rows[0], dataGrid.Columns[0]);
            dataGrid.UpdateLayout();

            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = dataGrid.Columns[0].GetCellContent(rows[0]) as TextBox;
            Assert.IsNotNull(editor);
            editor.Text = "Changed";
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            dataGrid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            Assert.AreEqual("Changed", rows[0].Name);
            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));
            Assert.IsTrue(GetOperations(dataGrid).Undo());
            Assert.AreEqual("A", rows[0].Name);
        });
    }

    private static DataGrid CreateGrid(ObservableCollection<TestRow> rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
        };
        dataGrid.Columns.Add(CreateColumn("name", nameof(TestRow.Name)));
        dataGrid.Columns.Add(CreateColumn("code", nameof(TestRow.Code)));
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        GridOperations.SetIsEnabled(dataGrid, true);
        return dataGrid;
    }

    private static DataGridTextColumn CreateColumn(string key, string path)
    {
        var column = new DataGridTextColumn
        {
            Header = key,
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
        };
        GridColumn.SetKey(column, key);
        GridColumn.SetFieldPath(column, path);
        return column;
    }

    private static void SelectRectangle(
        DataGrid dataGrid,
        int firstRow,
        int lastRow,
        int firstColumn,
        int lastColumn)
    {
        dataGrid.SelectedCells.Clear();
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                dataGrid.SelectedCells.Add(new DataGridCellInfo(dataGrid.Items[row], dataGrid.Columns[column]));
            }
        }
    }

    private static SwiftExport.Wpf.EnterpriseGrid.Operations.Internal.GridOperationsModule GetOperations(DataGrid dataGrid) =>
        EnterpriseGridCore.GetCoordinator(dataGrid)?.Operations ??
        throw new AssertFailedException("GridOperations 未启用。");

    private static IDisposable Show(DataGrid dataGrid)
    {
        var window = new Window
        {
            Width = 640,
            Height = 320,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid
        };
        window.Show();
        dataGrid.UpdateLayout();
        return new WindowScope(window);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class TestRow
    {
        public TestRow(string name, string code)
        {
            Name = name;
            Code = code;
        }

        public string Name { get; set; }

        public string Code { get; set; }
    }
}
