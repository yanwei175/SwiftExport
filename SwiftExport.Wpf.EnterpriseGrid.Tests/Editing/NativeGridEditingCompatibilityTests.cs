using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Selection;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Editing;

[TestClass]
public sealed class NativeGridEditingCompatibilityTests
{
    [TestMethod]
    public void SelectingAnotherCellAfterCommit_ShouldNotBeginEditingTargetCell()
    {
        StaTest.Run(() =>
        {
            var first = new TestRow { Name = "A" };
            var second = new TestRow { Name = "B" };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { first, second },
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
            };
            var column = new DataGridTextColumn { Binding = new Binding(nameof(TestRow.Name)) };
            GridColumn.SetKey(column, "name");
            dataGrid.Columns.Add(column);
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            GridSelection.SetIsCellClickSelectionEnabled(dataGrid, true);

            using var host = Show(dataGrid);
            dataGrid.CurrentCell = new DataGridCellInfo(first, column);
            dataGrid.ScrollIntoView(first, column);
            dataGrid.UpdateLayout();
            Assert.IsTrue(dataGrid.BeginEdit());
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));

            dataGrid.ScrollIntoView(second, column);
            dataGrid.UpdateLayout();
            var target = FindCell(dataGrid, second, column);
            Assert.IsNotNull(target);
            EnterpriseGridCore.GetCoordinator(dataGrid)!.Selection.SynchronizeCellClick(target);

            Assert.AreSame(second, dataGrid.CurrentCell.Item);
            Assert.IsFalse(target.IsEditing);
        });
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

    private static DataGridCell? FindCell(DependencyObject parent, object item, DataGridColumn column)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is DataGridCell cell && ReferenceEquals(cell.DataContext, item) && ReferenceEquals(cell.Column, column))
            {
                return cell;
            }

            if (FindCell(child, item, column) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class TestRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
