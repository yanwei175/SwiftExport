using SwiftExportClient.Resources.Behaviors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Resources.GridRegression;

/// <summary>验证框架不接管原生复制及编辑器输入，不提供提交历史。</summary>
[TestClass]
public sealed class NativeGridCommandsTests
{
    /// <summary>三种选择模式下复制、粘贴和 Delete 均由活动文本编辑器处理，编辑器内仍可撤销，提交后不产生 Grid 历史。</summary>
    [TestMethod]
    [DataRow(DataGridSelectionUnit.FullRow)]
    [DataRow(DataGridSelectionUnit.Cell)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader)]
    public void ClipboardAndDelete_EditingTextBox_ShouldKeepNativeOwnership(DataGridSelectionUnit unit)
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("ABC", "1") };
            var dataGrid = CreateGrid(rows);
            dataGrid.SelectionUnit = unit;
            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], dataGrid.Columns[0]);
            dataGrid.ScrollIntoView(rows[0], dataGrid.Columns[0]);
            dataGrid.UpdateLayout();

            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = dataGrid.Columns[0].GetCellContent(rows[0]) as TextBox;
            Assert.IsNotNull(editor);
            editor.Focus();
            editor.Select(1, 1);

            Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, editor));
            ApplicationCommands.Copy.Execute(null, editor);
            Assert.AreEqual("B", Clipboard.GetText());
            Assert.IsTrue(ApplicationCommands.Paste.CanExecute(null, editor));
            editor.SelectAll();
            ApplicationCommands.Paste.Execute(null, editor);
            Assert.AreEqual("B", editor.Text);
            editor.SelectAll();
            Assert.IsTrue(EditingCommands.Delete.CanExecute(null, editor));
            EditingCommands.Delete.Execute(null, editor);
            Assert.AreEqual(string.Empty, editor.Text);
            Assert.IsTrue(ApplicationCommands.Undo.CanExecute(null, editor));
            ApplicationCommands.Undo.Execute(null, editor);
            Assert.AreEqual("B", editor.Text);
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            dataGrid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Assert.AreEqual("B", rows[0].Name);
            Assert.AreEqual("1", rows[0].Code);
            Assert.IsFalse(ApplicationCommands.Undo.CanExecute(null, dataGrid));
        });
    }

    /// <summary>退出 Grid 粘贴和清空后，命令不写字段、不删行；启停增强模块不改变边界。</summary>
    [TestMethod]
    [DataRow(DataGridSelectionUnit.FullRow, true)]
    [DataRow(DataGridSelectionUnit.Cell, true)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader, true)]
    [DataRow(DataGridSelectionUnit.FullRow, false)]
    [DataRow(DataGridSelectionUnit.Cell, false)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader, false)]
    public void GridPasteAndDelete_ShouldNotMutateRows(DataGridSelectionUnit unit, bool enabled)
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A", "1"), new("B", "2") };
            var grid = CreateGrid(rows);
            GridRowHeader.SetShowRowNumber(grid, enabled);

            grid.SelectionUnit = unit;
            using var host = Show(grid);
            grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[0]);
            if (unit == DataGridSelectionUnit.FullRow) grid.SelectedItem = rows[0];
            else SelectRectangle(grid, 0, 1, 0, 1);
            grid.Focus();
            Clipboard.SetText("覆盖\t999");
            Assert.IsFalse(ApplicationCommands.Paste.CanExecute(null, grid));
            Assert.IsFalse(ApplicationCommands.Delete.CanExecute(null, grid));
            ApplicationCommands.Paste.Execute(null, grid);
            ApplicationCommands.Delete.Execute(null, grid);
            Assert.HasCount(2, rows);
            Assert.AreEqual("A", rows[0].Name);
            Assert.AreEqual("1", rows[0].Code);
            Assert.AreEqual("B", rows[1].Name);
            Assert.AreEqual("2", rows[1].Code);
            Assert.IsFalse(ApplicationCommands.Undo.CanExecute(null, grid));
        });
    }

    /// <summary>原生复制支持模板绑定、不连续及整行选区，并遵循视图排序、列重排和隐藏列。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void NativeCopy_ShouldUseNativeSelectionAndColumnBindings(bool enabled)
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A\n备注", "1"), new("B", "2") };
            var grid = CreateGrid(rows);
            GridRowHeader.SetShowRowNumber(grid, enabled);

            grid.IsReadOnly = true;
            grid.Items.SortDescriptions.Add(new SortDescription(nameof(TestRow.Code), ListSortDirection.Descending));
            var template = new DataGridTemplateColumn { ClipboardContentBinding = new Binding(nameof(TestRow.Code)) };
            GridValidation.SetFieldPath(template, nameof(TestRow.Name));
            grid.Columns.Add(template);
            template.DisplayIndex = 0;
            grid.Columns[1].Visibility = Visibility.Collapsed;
            using var host = Show(grid);
            grid.SelectedCells.Add(new DataGridCellInfo(rows[1], template));
            grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[0]));
            grid.Focus();
            Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, grid));
            ApplicationCommands.Copy.Execute(null, grid);
            Assert.AreEqual("2\t\r\n\tA\n备注\r\n", Clipboard.GetText());
            Assert.IsTrue(Clipboard.ContainsData(DataFormats.Html));
            Assert.IsTrue(Clipboard.ContainsData(DataFormats.CommaSeparatedValue));

            grid.SelectionUnit = DataGridSelectionUnit.FullRow;
            grid.SelectedItem = rows[0];
            grid.ScrollIntoView(rows[0]);
            grid.UpdateLayout();
            grid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(grid.SelectedCells.Count > 0);
            Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, grid));
            ApplicationCommands.Copy.Execute(null, grid);
            Assert.AreEqual("1\tA\n备注\r\n", Clipboard.GetText());
            grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
            Assert.IsFalse(ApplicationCommands.Copy.CanExecute(null, grid));
            Assert.IsFalse(ApplicationCommands.Undo.CanExecute(null, grid));
        });
    }

    private static DataGrid CreateGrid(ObservableCollection<TestRow> rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserDeleteRows = false,
            CanUserAddRows = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
        };
        dataGrid.Columns.Add(CreateColumn("name", nameof(TestRow.Name)));
        dataGrid.Columns.Add(CreateColumn("code", nameof(TestRow.Code)));

        return dataGrid;
    }

    private static DataGridTextColumn CreateColumn(string key, string path)
    {
        var column = new DataGridTextColumn
        {
            Header = key,
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
        };

        GridValidation.SetFieldPath(column, path);
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

    /// <summary>模拟具备多个关联字段的逻辑单元格。</summary>
}
