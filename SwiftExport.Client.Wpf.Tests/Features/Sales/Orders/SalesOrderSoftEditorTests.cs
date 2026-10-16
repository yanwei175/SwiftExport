using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

/// <summary>验证销售数量的客户端软提示不会阻断原生编辑提交。</summary>
[TestClass]
public sealed class SalesOrderSoftEditorTests
{
    /// <summary>保留无效原始输入并显示错误，修正后更新数量并清除对应列提示。</summary>
    [TestMethod]
    public void InvalidNumberText_ShouldCommitCellAndPreserveRawText()
    {
        StaTest.RunAsync(async () =>
        {
            var line = new SalesOrderLineModel();
            line.Load(new SalesOrderDetailLineDto
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
            GridValidation.SetIsEnabled(dataGrid, true);
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
                await dataGrid.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Assert.IsTrue(GridValidation.GetErrors(dataGrid).Any(error => ReferenceEquals(error.Column, numberColumn)));
                BeginEdit(dataGrid, line, numberColumn);
                textBox = FindVisualDescendant<TextBox>(dataGrid);
                textBox.Text = "3";
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.AreEqual(3m, line.Qty);
                await dataGrid.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Assert.IsFalse(GridValidation.GetErrors(dataGrid).Any(error => ReferenceEquals(error.Column, numberColumn)));
            }
            finally
            {
                window.Close();
            }

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
