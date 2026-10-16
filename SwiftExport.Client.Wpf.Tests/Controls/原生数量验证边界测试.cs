using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>验证入库数量的原生转换门禁和软提示彼此独立，使用客户端实际共享样式。</summary>
[TestClass]
public sealed class NativeQuantityValidationTests
{
    /// <summary>软提示开关不影响非法文本阻止提交、错误提示、修正和取消编辑。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConversionError_ShouldKeepNativeCommitAndTooltip(bool enableSoftValidation) =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var row = new InboundLineModel { Quantity = 2m };
            var column = new DataGridTextColumn
            {
                Binding = new Binding(nameof(InboundLineModel.Quantity))
                {
                    Mode = BindingMode.TwoWay, TargetNullValue = string.Empty,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ValidatesOnDataErrors = false, ValidatesOnNotifyDataErrors = false
                },
                EditingElementStyle = (Style)Application.Current.FindResource("SwiftExportDataGridNumberTextBox")
            };
            var grid = new DataGrid
            {
                Style = (Style)Application.Current.FindResource("SwiftExportDataGrid"),
                AutoGenerateColumns = false, CanUserAddRows = false, ItemsSource = new[] { row }
            };
            grid.Columns.Add(column);
            GridValidation.SetIsEnabled(grid, enableSoftValidation);
            var window = new Window { Content = grid, Width = 400, Height = 240, ShowInTaskbar = false };
            try
            {
                window.Show();
                grid.UpdateLayout();
                grid.CurrentCell = new DataGridCellInfo(row, column);
                Assert.IsTrue(grid.BeginEdit());
                var editor = (TextBox)column.GetCellContent(row);
                editor.Text = "abc";
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                await Drain();
                Assert.IsTrue(Validation.GetHasError(editor));
                Assert.IsFalse(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.AreEqual(2m, row.Quantity);
                Assert.AreEqual(0, GridValidation.GetErrorCount(grid), "原生转换错误不能二次进入模型软提示快照。");
                Assert.AreEqual(Validation.GetErrors(editor)[0].ErrorContent.ToString(), editor.ToolTip?.ToString());

                editor.Text = "3";
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                await Drain();
                Assert.IsFalse(Validation.GetHasError(editor));
                Assert.IsNull(editor.ToolTip);
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.AreEqual(3m, row.Quantity);

                Assert.IsTrue(grid.BeginEdit());
                editor = (TextBox)column.GetCellContent(row);
                editor.Text = "invalid";
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(grid.CancelEdit(DataGridEditingUnit.Cell));
                Assert.IsTrue(grid.BeginEdit());
                editor = (TextBox)column.GetCellContent(row);
                await Drain();
                Assert.IsFalse(Validation.GetHasError(editor));
                Assert.AreEqual(3m, row.Quantity);

                // 空数量属于模型业务错误，可以离开单元格，由软提示和页面保存校验处理。
                editor.Text = string.Empty;
                editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                await Drain();
                Assert.IsNull(row.Quantity);
                Assert.AreEqual(enableSoftValidation ? 1 : 0, GridValidation.GetErrorCount(grid));
            }
            finally { window.Close(); }
        });

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
}
