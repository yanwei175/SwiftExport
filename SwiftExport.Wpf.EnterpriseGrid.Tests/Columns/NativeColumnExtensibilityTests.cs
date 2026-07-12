using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Columns;

[TestClass]
public sealed class NativeColumnExtensibilityTests
{
    [TestMethod]
    public void CustomPopupEditorTemplate_ShouldExistOnlyForEditingCell()
    {
        StaTest.Run(() =>
        {
            var rows = new[] { new TestRow("A"), new TestRow("B") };
            var editingTemplate = new DataTemplate
            {
#pragma warning disable CS0618
                VisualTree = new FrameworkElementFactory(typeof(TestLookupEditor))
#pragma warning restore CS0618
            };
            var column = new DataGridTemplateColumn
            {
                CellTemplate = CreateDisplayTemplate(),
                CellEditingTemplate = editingTemplate
            };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
            dataGrid.Columns.Add(column);
            using var host = Show(dataGrid);

            Assert.AreEqual(0, CountVisualChildren<TestLookupEditor>(dataGrid));
            dataGrid.CurrentCell = new DataGridCellInfo(rows[0], column);
            dataGrid.ScrollIntoView(rows[0], column);
            dataGrid.UpdateLayout();
            Assert.IsTrue(dataGrid.BeginEdit());
            dataGrid.UpdateLayout();
            Assert.AreEqual(1, CountVisualChildren<TestLookupEditor>(dataGrid));

            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            dataGrid.UpdateLayout();
            Assert.AreEqual(0, CountVisualChildren<TestLookupEditor>(dataGrid));
        });
    }

    [TestMethod]
    public void HeaderButton_ShouldExecuteOwnCommandWithoutChangingSortDirection()
    {
        StaTest.Run(() =>
        {
            var executed = false;
            var command = new RoutedCommand();
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { new TestRow("A") } };
            dataGrid.CommandBindings.Add(new CommandBinding(command, (_, _) => executed = true));
            var button = new Button { Content = "筛选", Command = command, CommandTarget = dataGrid };
            var column = new DataGridTextColumn
            {
                Header = button,
                SortMemberPath = nameof(TestRow.Value)
            };
            dataGrid.Columns.Add(column);
            using var host = Show(dataGrid);

            command.Execute(button.CommandParameter, button.CommandTarget);

            Assert.IsTrue(executed);
            Assert.IsNull(column.SortDirection);
        });
    }

    private static DataTemplate CreateDisplayTemplate()
    {
        var template = new DataTemplate();
#pragma warning disable CS0618
        template.VisualTree = new FrameworkElementFactory(typeof(TextBlock));
#pragma warning restore CS0618
        return template;
    }

    private static int CountVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T)
            {
                count++;
            }

            count += CountVisualChildren<T>(child);
        }

        return count;
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

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    public sealed class TestLookupEditor : TextBox
    {
    }

    private sealed record TestRow(string Value);
}
