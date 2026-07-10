using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Editing;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Editing;

[TestClass]
public sealed class GridEditingTests
{
    [TestMethod]
    public void F2AndEnter_ShouldUseNativeEditTransactionAndStayOnCommittedCell()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A", "One");
            var dataGrid = CreateGrid(first);
            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, first, dataGrid.Columns[0]);

            var editing = GetEditing(dataGrid);
            Assert.IsTrue(editing.HandleKey(Key.F2, ModifierKeys.None, dataGrid));
            Assert.IsTrue(GetCurrentCell(dataGrid).IsEditing);

            Assert.IsTrue(editing.HandleKey(Key.Enter, ModifierKeys.None, dataGrid));
            Assert.IsFalse(GetCurrentCell(dataGrid).IsEditing);
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(dataGrid.Columns[0], dataGrid.CurrentCell.Column);

            Assert.IsTrue(editing.HandleKey(Key.Enter, ModifierKeys.None, dataGrid));
            Assert.IsTrue(GetCurrentCell(dataGrid).IsEditing);
        });
    }

    [TestMethod]
    public void DoubleClickAndF2_ShouldSupportNativeCheckBoxAndTemplateColumns()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A", "One") { IsActive = true };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { first },
                SelectionMode = DataGridSelectionMode.Single
            };
            var checkBoxColumn = new DataGridCheckBoxColumn
            {
                Header = "Active",
                Binding = new Binding(nameof(TestRow.IsActive))
            };
            GridColumn.SetKey(checkBoxColumn, "active");
            dataGrid.Columns.Add(checkBoxColumn);
            var templateColumn = new DataGridTemplateColumn
            {
                Header = "Template",
                CellTemplate = ParseTemplate("TextBlock"),
                CellEditingTemplate = ParseTemplate("TextBox")
            };
            GridColumn.SetKey(templateColumn, "template");
            dataGrid.Columns.Add(templateColumn);
            Enable(dataGrid);

            using var host = Show(dataGrid);
            var editing = GetEditing(dataGrid);

            SetCurrentCell(dataGrid, first, checkBoxColumn);
            Assert.IsTrue(editing.HandleKey(Key.F2, ModifierKeys.None, dataGrid));
            Assert.IsTrue(GetCurrentCell(dataGrid).IsEditing);
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));

            SetCurrentCell(dataGrid, first, templateColumn);
            var templateCell = GetCurrentCell(dataGrid);
            Assert.IsTrue(editing.HandleDoubleClick(templateCell));
            Assert.IsTrue(GetCurrentCell(dataGrid).IsEditing);

            Assert.IsFalse(editing.HandleKey(Key.Escape, ModifierKeys.None, dataGrid));
        });
    }

    [TestMethod]
    public void Tab_ShouldFollowDisplayIndexSkipUnavailableColumnsAndCrossRows()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A", "One");
            var second = new TestRow("B", "Two");
            var dataGrid = CreateGrid(first, second);
            var nameColumn = dataGrid.Columns[0];
            var codeColumn = dataGrid.Columns[1];
            var readOnlyColumn = AddTextColumn(dataGrid, "readonly", nameof(TestRow.Code));
            var hiddenColumn = AddTextColumn(dataGrid, "hidden", nameof(TestRow.Code));
            var disabledColumn = AddTextColumn(dataGrid, "disabled", nameof(TestRow.Code));

            readOnlyColumn.DisplayIndex = 0;
            readOnlyColumn.IsReadOnly = true;
            nameColumn.DisplayIndex = 1;
            hiddenColumn.DisplayIndex = 2;
            hiddenColumn.Visibility = Visibility.Collapsed;
            disabledColumn.DisplayIndex = 3;
            GridColumn.SetCanNavigate(disabledColumn, false);
            codeColumn.DisplayIndex = 4;

            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, first, nameColumn);
            var editing = GetEditing(dataGrid);

            Assert.IsTrue(editing.HandleKey(Key.Tab, ModifierKeys.None, dataGrid));
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(codeColumn, dataGrid.CurrentCell.Column);

            Assert.IsTrue(editing.HandleKey(Key.Tab, ModifierKeys.None, dataGrid));
            Assert.AreSame(second, dataGrid.CurrentCell.Item);
            Assert.AreSame(nameColumn, dataGrid.CurrentCell.Column);

            Assert.IsTrue(editing.HandleKey(Key.Tab, ModifierKeys.Shift, dataGrid));
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(codeColumn, dataGrid.CurrentCell.Column);
        });
    }

    [TestMethod]
    public void Tab_WhenValidationFails_ShouldRemainOnCurrentCell()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A", "One");
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { first },
                SelectionMode = DataGridSelectionMode.Single
            };
            var validatedColumn = new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Binding(nameof(TestRow.Name))
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                    ValidationRules = { new RequiredRule() }
                }
            };
            GridColumn.SetKey(validatedColumn, "name");
            dataGrid.Columns.Add(validatedColumn);
            AddTextColumn(dataGrid, "code", nameof(TestRow.Code));
            Enable(dataGrid);

            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, first, validatedColumn);
            var editing = GetEditing(dataGrid);
            Assert.IsTrue(editing.HandleKey(Key.F2, ModifierKeys.None, dataGrid));

            var editor = validatedColumn.GetCellContent(first) as TextBox;
            Assert.IsNotNull(editor);
            editor.Text = string.Empty;

            Assert.IsTrue(editing.HandleKey(Key.Tab, ModifierKeys.None, editor));
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(validatedColumn, dataGrid.CurrentCell.Column);
            Assert.IsTrue(Validation.GetHasError(editor));
        });
    }

    [TestMethod]
    public void MultilineEditor_ShouldKeepEnterAndTabOwnership()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A", "One");
            var dataGrid = CreateGrid(first);
            var editor = new TextBox { AcceptsReturn = true, AcceptsTab = true };
            var editing = GetEditing(dataGrid);

            Assert.IsFalse(editing.HandleKey(Key.Enter, ModifierKeys.None, editor));
            Assert.IsFalse(editing.HandleKey(Key.Tab, ModifierKeys.None, editor));
        });
    }

    [TestMethod]
    public void DisabledFeature_ShouldLeaveNativeBehaviorUntouched()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow("A", "One");
            var dataGrid = CreateGrid(first);
            GridEditing.SetIsEnabled(dataGrid, false);
            SetCurrentCell(dataGrid, first, dataGrid.Columns[0]);

            Assert.IsFalse(GetEditing(dataGrid).HandleKey(Key.Enter, ModifierKeys.None, dataGrid));
            Assert.AreSame(first, dataGrid.CurrentCell.Item);
            Assert.AreSame(dataGrid.Columns[0], dataGrid.CurrentCell.Column);
        });
    }

    private static DataGrid CreateGrid(params TestRow[] rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Single
        };
        AddTextColumn(dataGrid, "name", nameof(TestRow.Name));
        AddTextColumn(dataGrid, "code", nameof(TestRow.Code));
        Enable(dataGrid);
        return dataGrid;
    }

    private static DataGridTextColumn AddTextColumn(DataGrid dataGrid, string key, string path)
    {
        var column = new DataGridTextColumn
        {
            Header = key,
            Binding = new Binding(path)
        };
        GridColumn.SetKey(column, key);
        dataGrid.Columns.Add(column);
        return column;
    }

    private static DataTemplate ParseTemplate(string elementName) =>
        (DataTemplate)XamlReader.Parse(
            $$"""
             <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                 <{{elementName}} Text="{Binding Code}" />
             </DataTemplate>
             """);

    private static void Enable(DataGrid dataGrid)
    {
        GridEditing.SetIsEnabled(dataGrid, true);
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
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

    private static void SetCurrentCell(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.SelectedItem = item;
        dataGrid.CurrentCell = new DataGridCellInfo(item, column);
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
    }

    private static DataGridCell GetCurrentCell(DataGrid dataGrid)
    {
        dataGrid.UpdateLayout();
        return FindCurrentCell(dataGrid, dataGrid) ??
               throw new AssertFailedException("当前 DataGridCell 未实现。");
    }

    private static DataGridCell? FindCurrentCell(DependencyObject parent, DataGrid dataGrid)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is DataGridCell cell &&
                ReferenceEquals(cell.DataContext, dataGrid.CurrentCell.Item) &&
                ReferenceEquals(cell.Column, dataGrid.CurrentCell.Column))
            {
                return cell;
            }

            if (FindCurrentCell(child, dataGrid) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static SwiftExport.Wpf.EnterpriseGrid.Editing.Internal.GridEditingModule GetEditing(DataGrid dataGrid) =>
        EnterpriseGridCore.GetCoordinator(dataGrid)?.Editing ??
        throw new AssertFailedException("Enterprise Grid 协调器未启用。");

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class RequiredRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
            string.IsNullOrWhiteSpace(value?.ToString())
                ? new ValidationResult(false, "必填")
                : ValidationResult.ValidResult;
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

        public bool IsActive { get; set; }
    }
}
