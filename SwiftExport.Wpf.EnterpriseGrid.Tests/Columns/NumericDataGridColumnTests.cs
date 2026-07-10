using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Numeric;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Columns;

[TestClass]
public sealed class NumericDataGridColumnTests
{
    [TestMethod]
    public void Commit_ShouldValidateFormatRangeAndDecimalPlacesWithoutFilteringInput()
    {
        StaTest.Run(() =>
        {
            var row = new NumericRow { Price = 1m };
            var column = new NumericDataGridColumn
            {
                Binding = new Binding(nameof(NumericRow.Price)),
                Minimum = 0m,
                Maximum = 100m,
                MaximumDecimalPlaces = 2
            };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { row },
                Language = XmlLanguage.GetLanguage("en-US")
            };
            dataGrid.Columns.Add(column);
            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, row, column);

            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = FindCurrentEditor(dataGrid);
            editor.Text = "101";
            Assert.IsFalse(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.IsTrue(System.Windows.Controls.Validation.GetHasError(editor));

            editor.Text = "10.123";
            Assert.IsFalse(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));

            editor.Text = "10.12";
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.AreEqual(10.12m, row.Price);
            Assert.IsFalse(System.Windows.Controls.Validation.GetHasError(editor));
        });
    }

    private static IDisposable Show(DataGrid dataGrid)
    {
        var window = new Window
        {
            Width = 400,
            Height = 200,
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

    private static TextBox FindCurrentEditor(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBox textBox)
            {
                return textBox;
            }

            try
            {
                return FindCurrentEditor(child);
            }
            catch (AssertFailedException)
            {
            }
        }

        throw new AssertFailedException("未找到 Numeric 编辑器。");
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class NumericRow
    {
        public decimal Price { get; set; }
    }
}
