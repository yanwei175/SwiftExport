using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderSoftEditorTests
{
    [TestMethod]
    public void InvalidNumberAndDateText_ShouldCommitCellAndPreserveRawText()
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
                ExpectedDeliveryDate = new DateTime(2026, 8, 1),
            });
            var numberColumn = new DataGridTextColumn
            {
                Binding = new Binding(nameof(SalesOrderLineModel.QtyText))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                },
            };
            var dateColumn = new DataGridTemplateColumn
            {
                CellTemplate = CreateDateTemplate(editing: false),
                CellEditingTemplate = CreateDateTemplate(editing: true),
            };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                ItemsSource = new[] { line },
            };
            dataGrid.Columns.Add(numberColumn);
            dataGrid.Columns.Add(dateColumn);
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

                BeginEdit(dataGrid, line, dateColumn);
                var datePicker = FindVisualDescendant<SoftInputDatePicker>(dataGrid);
                datePicker.Text = "invalid-date";
                Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.AreEqual("invalid-date", line.ExpectedDeliveryDateText);
                Assert.AreEqual(line.ExpectedDeliveryDate, (DateTime?)new DateTime(2026, 8, 1));

                BeginEdit(dataGrid, line, dateColumn);
                datePicker = FindVisualDescendant<SoftInputDatePicker>(dataGrid);
                datePicker.Text = "2026-09-15";
                Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.AreEqual("2026-09-15", line.ExpectedDeliveryDateText);
                Assert.AreEqual(line.ExpectedDeliveryDate, (DateTime?)new DateTime(2026, 9, 15));

                BeginEdit(dataGrid, line, dateColumn);
                datePicker = FindVisualDescendant<SoftInputDatePicker>(dataGrid);
                datePicker.Text = string.Empty;
                Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.IsEmpty(line.ExpectedDeliveryDateText);
                Assert.IsNull(line.ExpectedDeliveryDate);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    private static DataTemplate CreateDateTemplate(bool editing)
    {
        var template = new DataTemplate();
#pragma warning disable CS0618
        var factory = new FrameworkElementFactory(editing ? typeof(SoftInputDatePicker) : typeof(TextBlock));
#pragma warning restore CS0618
        var property = editing ? SoftInputDatePicker.RawTextProperty : TextBlock.TextProperty;
        factory.SetBinding(property, new Binding(nameof(SalesOrderLineModel.ExpectedDeliveryDateText))
        {
            Mode = editing ? BindingMode.TwoWay : BindingMode.OneWay,
            UpdateSourceTrigger = editing ? UpdateSourceTrigger.PropertyChanged : UpdateSourceTrigger.Default,
        });
        template.VisualTree = factory;
        return template;
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
