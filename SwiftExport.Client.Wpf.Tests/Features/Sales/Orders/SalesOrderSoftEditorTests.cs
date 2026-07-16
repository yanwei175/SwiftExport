using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Sales.Orders.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderSoftEditorTests
{
    [TestMethod]
    public void InvalidNumberText_ShouldCommitCellAndPreserveRawText()
    {
        StaTest.RunAsync(() =>
        {
            var line = new SalesOrderLineModel();
            line.Load(new SalesOrderDocumentLineDto
            {
                Uid = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                Sku = "STD-001",
                Qty = 2m,
            });
            var numberColumn = new DataGridTextColumn
            {
                Binding = new Binding(nameof(SalesOrderLineModel.QtyText))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                },
            };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                ItemsSource = new[] { line },
            };
            dataGrid.Columns.Add(numberColumn);
            var window = new Window
            {
                Width = 420,
                Height = 180,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = dataGrid,
            };

            try
            {
                window.Show();
                dataGrid.UpdateLayout();

                BeginEdit(dataGrid, line, numberColumn);
                var textBox = FindVisualDescendant<TextBox>(dataGrid);
                textBox.Text = "invalid-number";
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.AreEqual("invalid-number", line.QtyText);
                Assert.AreEqual(2m, line.Qty);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    private static void BeginEdit(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.CurrentCell = new DataGridCellInfo(item, column);
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
        Assert.IsTrue(dataGrid.BeginEdit());
        dataGrid.UpdateLayout();
    }

    private static T FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            try
            {
                return FindVisualDescendant<T>(child);
            }
            catch (AssertFailedException)
            {
            }
        }

        throw new AssertFailedException($"未找到 {typeof(T).Name} 编辑器。");
    }
}
