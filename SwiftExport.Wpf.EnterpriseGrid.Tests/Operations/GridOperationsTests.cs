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
/// <summary>验证 Grid 批量操作的准入、原子写入及撤销契约。</summary>
public sealed class GridOperationsTests
{
    /// <summary>列级开关只限制填充，重新启用后恢复既有行为。</summary>
    [TestMethod]
    public void FillDown_ColumnSwitch_ShouldSkipColumnAndSupportReenable()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1"), new("B", "2") };
            var grid = CreateGrid(rows);
            using var host = Show(grid);
            SelectRectangle(grid, 0, 1, 0, 1);
            Assert.IsTrue(GridColumn.GetCanFillDown(grid.Columns[0]));
            GridColumn.SetCanFillDown(grid.Columns[0], false);
            var operations = GetOperations(grid);
            Assert.IsTrue(operations.FillDown());
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual("1", rows[1].Code);
            Assert.IsTrue(operations.Undo());
            GridColumn.SetCanFillDown(grid.Columns[1], false);
            Assert.IsFalse(operations.CanFillDown());
            Assert.IsFalse(operations.FillDown());
            Assert.IsFalse(GridOperations.GetCanUndo(grid));
            GridColumn.SetCanFillDown(grid.Columns[0], true);
            Assert.IsTrue(operations.FillDown());
            Assert.AreEqual("A", rows[1].Name);
            Assert.AreEqual("2", rows[1].Code);
        });
    }

    /// <summary>逐目标读取布尔状态，源行不可编辑仍可提供值，执行时重新检查最新状态。</summary>
    [TestMethod]
    public void FillDown_RowPermission_ShouldSkipBlockedTargetsAndRecheckBeforeExecution()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("A", "1") { CanEdit = false },
                new("B", "2") { CanEdit = false },
                new("C", "3")
            };
            var grid = CreateGrid(rows);
            GridColumn.SetCanFillDownPath(grid.Columns[0], nameof(TestRow.CanEdit));
            using var host = Show(grid);
            SelectRectangle(grid, 0, 2, 0, 1);
            var operations = GetOperations(grid);
            Assert.IsTrue(operations.FillDown());
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual("A", rows[2].Name);
            Assert.AreEqual("1", rows[1].Code);
            Assert.AreEqual("1", rows[2].Code);
            Assert.IsTrue(operations.Undo());
            Assert.AreEqual("C", rows[2].Name);
            Assert.AreEqual("2", rows[1].Code);
            SelectRectangle(grid, 0, 2, 0, 0);
            Assert.IsTrue(operations.CanFillDown());
            rows[2].CanEdit = false;
            Assert.IsFalse(operations.FillDown());
            Assert.IsFalse(GridOperations.GetCanUndo(grid));
            rows[1].CanEdit = true;
            Assert.IsTrue(operations.FillDown());
            Assert.AreEqual("A", rows[1].Name);
            Assert.AreEqual("C", rows[2].Name);
        });
    }

    /// <summary>错误路径、非布尔、空值与异常 getter 都不能放行目标写入。</summary>
    [TestMethod]
    [DataRow("Missing")]
    [DataRow("Name")]
    [DataRow("OptionalPermission")]
    [DataRow("BrokenPermission")]
    public void FillDown_InvalidPermission_ShouldFailClosed(string path)
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1"), new("B", "2") };
            var grid = CreateGrid(rows);
            GridColumn.SetCanFillDownPath(grid.Columns[0], path);
            using var host = Show(grid);
            SelectRectangle(grid, 0, 1, 0, 0);
            Assert.IsFalse(GetOperations(grid).CanFillDown());
            Assert.IsFalse(GetOperations(grid).FillDown());
            Assert.AreEqual("B", rows[1].Name);
            Assert.IsFalse(GridOperations.GetCanUndo(grid));
        });
    }

    /// <summary>复合列以目标 Cell 为单位跳过，不依赖屏幕外的虚拟化容器。</summary>
    [TestMethod]
    public void FillDown_CompositePermission_ShouldWorkForUnrealizedRowsAndUndo()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<CompositeLookupRow>(Enumerable.Range(0, 100)
                .Select(index => new CompositeLookupRow(Guid.NewGuid(), $"S{index}", $"N{index}")));
            var lastId = rows[99].SupplierId;
            rows[98].CanEdit = false;
            var blockedId = rows[98].SupplierId;
            var column = new DataGridTemplateColumn();
            GridColumn.SetFillDownFieldPaths(column, "SupplierId,SupplierCode,SupplierName");
            GridColumn.SetCanFillDownPath(column, nameof(CompositeLookupRow.CanEdit));
            var grid = CreateCompositeLookupGrid(rows, column);
            using var host = Show(grid);
            SelectRectangle(grid, 0, 99, 0, 0);
            Assert.IsNull(grid.ItemContainerGenerator.ContainerFromItem(rows[99]));
            Assert.IsTrue(GetOperations(grid).FillDown());
            Assert.AreEqual(blockedId, rows[98].SupplierId);
            Assert.AreEqual("S98", rows[98].SupplierCode);
            Assert.AreEqual("N98", rows[98].SupplierName);
            Assert.AreEqual(rows[0].SupplierId, rows[99].SupplierId);
            Assert.AreEqual("S0", rows[99].SupplierCode);
            Assert.AreEqual("N0", rows[99].SupplierName);
            Assert.IsNull(grid.ItemContainerGenerator.ContainerFromItem(rows[99]));
            Assert.IsTrue(GetOperations(grid).Undo());
            Assert.AreEqual(lastId, rows[99].SupplierId);
            Assert.AreEqual("S99", rows[99].SupplierCode);
            Assert.AreEqual("N99", rows[99].SupplierName);
        });
    }

    /// <summary>首个字段改变许可后仍完成同一逻辑 Cell，避免复合关系只写一半。</summary>
    [TestMethod]
    public void FillDown_CompositePermissionChangingDuringWrite_ShouldUseInitialPermission()
    {
        StaTest.Run(() =>
        {
            var targetId = Guid.NewGuid();
            var rows = new ObservableCollection<CompositeLookupRow>
            {
                new(Guid.NewGuid(), "SUP", "Supplier"),
                new(targetId, null, "Original")
            };
            var column = new DataGridTemplateColumn();
            GridColumn.SetFillDownFieldPaths(column, "SupplierCode,SupplierId,SupplierName");
            GridColumn.SetCanFillDownPath(column, nameof(CompositeLookupRow.IsSupplierEmpty));
            var grid = CreateCompositeLookupGrid(rows, column);
            using var host = Show(grid);
            SelectRectangle(grid, 0, 1, 0, 0);
            Assert.IsTrue(GetOperations(grid).FillDown());
            Assert.IsFalse(rows[1].IsSupplierEmpty);
            Assert.AreEqual(rows[0].SupplierId, rows[1].SupplierId);
            Assert.AreEqual("Supplier", rows[1].SupplierName);
            Assert.IsTrue(GetOperations(grid).Undo());
            Assert.IsNull(rows[1].SupplierCode);
            Assert.AreEqual(targetId, rows[1].SupplierId);
            Assert.AreEqual("Original", rows[1].SupplierName);
        });
    }

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
    public void FillDown_CompositeTemplateColumn_ShouldCopyAllFieldsAndUndoAtomically()
    {
        StaTest.Run(() =>
        {
            var sourceId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var rows = new ObservableCollection<CompositeLookupRow>
            {
                new(sourceId, "SUP-A", "Supplier A"),
                new(targetId, "SUP-B", "Supplier B")
            };
            var column = new DataGridTemplateColumn { Header = "supplier" };
            GridColumn.SetKey(column, "supplier");
            GridColumn.SetFillDownFieldPaths(
                column,
                "SupplierId, SupplierCode | SupplierName;SupplierCode");
            var dataGrid = CreateCompositeLookupGrid(rows, column);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 1, 0, 0);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], column);

            var operations = GetOperations(dataGrid);
            Assert.IsTrue(operations.CanFillDown());
            Assert.IsTrue(operations.FillDown());
            Assert.AreEqual(sourceId, rows[1].SupplierId);
            Assert.AreEqual("SUP-A", rows[1].SupplierCode);
            Assert.AreEqual("Supplier A", rows[1].SupplierName);
            Assert.IsTrue(GridOperations.GetCanUndo(dataGrid));

            // 复合填充元数据不能顺带开放 Grid 级 Copy/Paste/Delete。
            Assert.IsFalse(operations.CanCopy());
            Assert.IsFalse(operations.CanPasteText("SUP-X"));
            Assert.IsFalse(operations.CanClearSelectedCells());

            Assert.IsTrue(operations.Undo());
            Assert.AreEqual(targetId, rows[1].SupplierId);
            Assert.AreEqual("SUP-B", rows[1].SupplierCode);
            Assert.AreEqual("Supplier B", rows[1].SupplierName);
            Assert.IsFalse(GridOperations.GetCanUndo(dataGrid));
        });
    }

    [TestMethod]
    public void FillDown_CompositeTemplateColumnWithInvalidPath_ShouldNotModifyAnyField()
    {
        StaTest.Run(() =>
        {
            var sourceId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var rows = new ObservableCollection<CompositeLookupRow>
            {
                new(sourceId, "SUP-A", "Supplier A"),
                new(targetId, "SUP-B", "Supplier B")
            };
            var column = new DataGridTemplateColumn { Header = "supplier" };
            GridColumn.SetFillDownFieldPaths(column, "SupplierId,MissingPath,SupplierName");
            var dataGrid = CreateCompositeLookupGrid(rows, column);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 1, 0, 0);

            var operations = GetOperations(dataGrid);
            Assert.IsFalse(operations.CanFillDown());
            Assert.IsFalse(operations.FillDown());
            Assert.AreEqual(targetId, rows[1].SupplierId);
            Assert.AreEqual("SUP-B", rows[1].SupplierCode);
            Assert.AreEqual("Supplier B", rows[1].SupplierName);
            Assert.IsFalse(GridOperations.GetCanUndo(dataGrid));
        });
    }

    [TestMethod]
    public void FillDown_CompositeTemplateColumnSetterFailure_ShouldRollBackWholeOperation()
    {
        StaTest.Run(() =>
        {
            var sourceId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var rows = new ObservableCollection<CompositeLookupRow>
            {
                new(sourceId, "FAIL", "Supplier A"),
                new(targetId, "SUP-B", "Supplier B") { ThrowOnSupplierCodeSet = true }
            };
            var column = new DataGridTemplateColumn { Header = "supplier" };
            GridColumn.SetFillDownFieldPaths(column, "SupplierId,SupplierCode,SupplierName");
            var dataGrid = CreateCompositeLookupGrid(rows, column);
            using var host = Show(dataGrid);
            SelectRectangle(dataGrid, 0, 1, 0, 0);

            var operations = GetOperations(dataGrid);
            Assert.IsTrue(operations.CanFillDown());
            Assert.IsFalse(operations.FillDown());
            Assert.AreEqual(targetId, rows[1].SupplierId);
            Assert.AreEqual("SUP-B", rows[1].SupplierCode);
            Assert.AreEqual("Supplier B", rows[1].SupplierName);
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

    private static DataGrid CreateCompositeLookupGrid(
        ObservableCollection<CompositeLookupRow> rows,
        DataGridTemplateColumn column)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
        };
        dataGrid.Columns.Add(column);
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

    /// <summary>提供文本字段和可动态变化的填充许可测试数据。</summary>
    private sealed class TestRow
    {
        public TestRow(string name, string code)
        {
            Name = name;
            Code = code;
        }

        public string Name { get; set; }

        public string Code { get; set; }

        public bool CanEdit { get; set; } = true;

        public bool? OptionalPermission => null;

        /// <summary>模拟许可求值失败，验证命令查询安全降级。</summary>
        public bool BrokenPermission => throw new InvalidOperationException("模拟许可读取失败。");
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

    /// <summary>模拟具备多个关联字段的逻辑单元格。</summary>
    private sealed class CompositeLookupRow
    {
        private string? _supplierCode;

        public CompositeLookupRow(Guid supplierId, string? supplierCode, string? supplierName)
        {
            SupplierId = supplierId;
            _supplierCode = supplierCode;
            SupplierName = supplierName;
        }

        public Guid SupplierId { get; set; }

        public bool CanEdit { get; set; } = true;

        public string? SupplierCode
        {
            get => _supplierCode;
            set
            {
                if (ThrowOnSupplierCodeSet && string.Equals(value, "FAIL", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("模拟复合字段写入失败。");
                }

                _supplierCode = value;
            }
        }

        public string? SupplierName { get; set; }

        /// <summary>模拟随填充字段变化的派生许可。</summary>
        public bool IsSupplierEmpty => SupplierCode is null;

        public bool ThrowOnSupplierCodeSet { get; init; }
    }

    private enum TestKind
    {
        First,
        Second
    }
}
