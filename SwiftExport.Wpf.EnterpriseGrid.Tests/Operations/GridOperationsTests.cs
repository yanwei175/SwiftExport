using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
    public void ClipboardCommand_EditingTextBox_ShouldKeepNativeOwnershipInFullRowMode()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("ABC", "1") };
            var dataGrid = CreateGrid(rows);
            dataGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            dataGrid.ScrollIntoView(rows[0], dataGrid.Columns[0]);
            dataGrid.UpdateLayout();

            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = dataGrid.Columns[0].GetCellContent(rows[0]) as TextBox;
            Assert.IsNotNull(editor);
            editor.Focus();
            editor.SelectAll();

            Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, editor));
            ApplicationCommands.Copy.Execute(null, editor);
            Assert.IsTrue(ApplicationCommands.Paste.CanExecute(null, editor));
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

    [TestMethod]
    public void Copy_Rectangle_ShouldCreateExcelCompatibleTsvWithEscaping()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("A", "1"),
                new("line\twith\n\"quote\"", "2")
            };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 1, 0, 1);

            var operations = GetOperations(dataGrid);
            Assert.IsTrue(operations.CanCopy());
            Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, dataGrid));
            Assert.IsTrue(operations.TryBuildCopyText(out var text));
            Assert.AreEqual("A\t1\r\n\"line\twith\n\"\"quote\"\"\"\t2", text);
        });
    }

    [TestMethod]
    public void Copy_NonRectangleFullRowOrFieldlessColumn_ShouldBeDisabled()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1"), new("B", "2") };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.SelectedCells.Clear();
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[0], dataGrid.Columns[0]));
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[1], dataGrid.Columns[1]));
            Assert.IsFalse(GetOperations(dataGrid).CanCopy());

            dataGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            Assert.IsFalse(GetOperations(dataGrid).CanCopy());
            dataGrid.Focus();
            Assert.IsFalse(ApplicationCommands.Copy.CanExecute(null, dataGrid));
            Assert.IsFalse(ApplicationCommands.Paste.CanExecute(null, dataGrid));

            dataGrid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
            var fieldless = new DataGridTemplateColumn { Header = "image" };
            dataGrid.Columns.Add(fieldless);
            dataGrid.SelectedCells.Clear();
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[0], fieldless));
            Assert.IsFalse(GetOperations(dataGrid).CanCopy());
        });
    }

    [TestMethod]
    public void Copy_SortedView_ShouldUseCurrentViewOrder()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("B", "2"), new("A", "1") };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.Items.SortDescriptions.Add(
                new SortDescription(nameof(TestRow.Name), ListSortDirection.Ascending));
            SelectRectangle(dataGrid, 0, 1, 0, 0);

            Assert.IsTrue(GetOperations(dataGrid).TryBuildCopyText(out var text));
            Assert.AreEqual("A\r\nB", text);
        });
    }

    [TestMethod]
    public void Paste_Matrix_ShouldConvertTypesSelectRangeAndUndoAtomically()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<PasteRow>
            {
                new("A", 1, TestKind.First),
                new("B", 2, TestKind.Second)
            };
            var dataGrid = CreatePasteGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);

            var operations = GetOperations(dataGrid);
            Assert.IsTrue(operations.CanPasteText("X\t10\tSecond\r\nY\t20\tFirst"));
            Assert.IsTrue(operations.PasteText("X\t10\tSecond\r\nY\t20\tFirst"));
            Assert.AreEqual("X", rows[0].Name);
            Assert.AreEqual(10, rows[0].Quantity);
            Assert.AreEqual(TestKind.Second, rows[0].Kind);
            Assert.AreEqual("Y", rows[1].Name);
            Assert.AreEqual(20, rows[1].Quantity);
            Assert.AreEqual(TestKind.First, rows[1].Kind);
            Assert.AreEqual(6, dataGrid.SelectedCells.Count);
            Assert.AreSame(rows[0], dataGrid.CurrentCell.Item);
            Assert.AreSame(dataGrid.Columns[0], dataGrid.CurrentCell.Column);
            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));

            Assert.IsTrue(operations.Undo());
            Assert.AreEqual("A", rows[0].Name);
            Assert.AreEqual(1, rows[0].Quantity);
            Assert.AreEqual(TestKind.First, rows[0].Kind);
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual(2, rows[1].Quantity);
            Assert.AreEqual(TestKind.Second, rows[1].Kind);
        });
    }

    [TestMethod]
    public void Paste_SingleValue_ShouldFillSelectedRectangle()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<PasteRow>
            {
                new("A", 1, TestKind.First),
                new("B", 2, TestKind.First),
                new("C", 3, TestKind.First)
            };
            var dataGrid = CreatePasteGrid(rows);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 2, 1, 1);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[1], dataGrid.Columns[1]);

            Assert.IsTrue(GetOperations(dataGrid).PasteText("9"));
            CollectionAssert.AreEqual(new[] { 9, 9, 9 }, rows.Select(row => row.Quantity).ToArray());
            Assert.AreEqual(3, dataGrid.SelectedCells.Count);
            Assert.AreSame(rows[0], dataGrid.CurrentCell.Item);
        });
    }

    [TestMethod]
    public void Paste_InvalidConversionReadOnlyOrOutOfBounds_ShouldRejectWithoutPartialChanges()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<PasteRow> { new("A", 1, TestKind.First) };
            var dataGrid = CreatePasteGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            var operations = GetOperations(dataGrid);

            Assert.IsFalse(operations.PasteText("Changed\tnot-an-int"));
            Assert.AreEqual("A", rows[0].Name);
            Assert.AreEqual(1, rows[0].Quantity);

            dataGrid.Columns[1].IsReadOnly = true;
            Assert.IsFalse(operations.PasteText("Changed\t2"));
            Assert.AreEqual("A", rows[0].Name);

            dataGrid.Columns[1].IsReadOnly = false;
            Assert.IsFalse(operations.PasteText("Changed\t2\tSecond\textra"));
            Assert.AreEqual("A", rows[0].Name);
            Assert.IsFalse(GridOperations.GetCanUndo(dataGrid));
        });
    }

    [TestMethod]
    public void Paste_HiddenColumnShouldBeSkippedAndFieldlessTargetShouldReject()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<PasteRow> { new("A", 1, TestKind.First) };
            var dataGrid = CreatePasteGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.Columns[1].Visibility = Visibility.Collapsed;
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);

            Assert.IsTrue(GetOperations(dataGrid).PasteText("X\tSecond"));
            Assert.AreEqual("X", rows[0].Name);
            Assert.AreEqual(1, rows[0].Quantity);
            Assert.AreEqual(TestKind.Second, rows[0].Kind);
            Assert.IsTrue(GetOperations(dataGrid).Undo());

            var fieldless = new DataGridTemplateColumn { Header = "fieldless", DisplayIndex = 1 };
            dataGrid.Columns.Add(fieldless);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            Assert.IsFalse(GetOperations(dataGrid).PasteText("Changed\tvalue"));
            Assert.AreEqual("A", rows[0].Name);
        });
    }

    [TestMethod]
    public void Delete_SelectedTextCells_ShouldClearNonContiguousSelectionAndUndoAtomically()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1"), new("B", "2") };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.SelectedCells.Clear();
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[0], dataGrid.Columns[0]));
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[1], dataGrid.Columns[1]));
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            var operations = GetOperations(dataGrid);

            Assert.IsTrue(operations.CanClearSelectedCells());
            Assert.IsTrue(ApplicationCommands.Delete.CanExecute(null, dataGrid));
            Assert.IsTrue(operations.ClearSelectedCells());
            Assert.AreEqual(string.Empty, rows[0].Name);
            Assert.AreEqual("1", rows[0].Code);
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual(string.Empty, rows[1].Code);
            Assert.AreEqual(2, dataGrid.SelectedCells.Count);
            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));

            Assert.IsTrue(operations.Undo());
            Assert.AreEqual("A", rows[0].Name);
            Assert.AreEqual("2", rows[1].Code);
        });
    }

    [TestMethod]
    public void Delete_TextColumns_ShouldUseEmptyNullAndDefaultValues()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<ClearRow>
            {
                new() { Text = "value", OptionalNumber = 7, Number = 9 }
            };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = rows,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
            };
            dataGrid.Columns.Add(CreateColumn("text", nameof(ClearRow.Text)));
            dataGrid.Columns.Add(CreateColumn("optional", nameof(ClearRow.OptionalNumber)));
            dataGrid.Columns.Add(CreateColumn("number", nameof(ClearRow.Number)));
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            GridOperations.SetIsEnabled(dataGrid, true);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 0, 0, 2);

            Assert.IsTrue(GetOperations(dataGrid).ClearSelectedCells());
            Assert.AreEqual(string.Empty, rows[0].Text);
            Assert.IsNull(rows[0].OptionalNumber);
            Assert.AreEqual(0, rows[0].Number);
        });
    }

    [TestMethod]
    public void Delete_ReadOnlyTemplateOrFullRow_ShouldRejectWithoutPartialChanges()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1") };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);
            dataGrid.Columns[1].IsReadOnly = true;
            SelectRectangle(dataGrid, 0, 0, 0, 1);
            var operations = GetOperations(dataGrid);

            Assert.IsFalse(operations.CanClearSelectedCells());
            Assert.IsFalse(operations.ClearSelectedCells());
            Assert.AreEqual("A", rows[0].Name);
            Assert.AreEqual("1", rows[0].Code);

            dataGrid.Columns[1].IsReadOnly = false;
            var templateColumn = new DataGridTemplateColumn { Header = "template" };
            GridColumn.SetFieldPath(templateColumn, nameof(TestRow.Name));
            dataGrid.Columns.Add(templateColumn);
            dataGrid.SelectedCells.Clear();
            dataGrid.SelectedCells.Add(new DataGridCellInfo(rows[0], templateColumn));
            Assert.IsFalse(operations.CanClearSelectedCells());

            dataGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            dataGrid.SelectedItem = rows[0];
            dataGrid.Focus();
            Assert.IsFalse(operations.CanClearSelectedCells());
            Assert.IsFalse(ApplicationCommands.Delete.CanExecute(null, dataGrid));
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

    private static DataGrid CreatePasteGrid(ObservableCollection<PasteRow> rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
        };
        dataGrid.Columns.Add(CreateColumn("name", nameof(PasteRow.Name)));
        dataGrid.Columns.Add(CreateColumn("quantity", nameof(PasteRow.Quantity)));
        dataGrid.Columns.Add(CreateColumn("kind", nameof(PasteRow.Kind)));
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

    private sealed class PasteRow(string name, int quantity, TestKind kind)
    {
        public string Name { get; set; } = name;

        public int Quantity { get; set; } = quantity;

        public TestKind Kind { get; set; } = kind;
    }

    private sealed class ClearRow
    {
        public string? Text { get; set; }

        public int? OptionalNumber { get; set; }

        public int Number { get; set; }
    }

    private enum TestKind
    {
        First,
        Second
    }
}
