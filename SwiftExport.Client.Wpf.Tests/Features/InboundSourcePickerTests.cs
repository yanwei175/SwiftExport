using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证独立选源组件的勾选及真实模板绑定，不调用后端。</summary>
[TestClass]
public sealed class InboundSourcePickerTests
{
    /// <summary>全选和取消只改变勾选，不修改来源数量；空集合可安全操作。</summary>
    [TestMethod]
    public void ToggleAll_PreservesQuantities()
    {
        var first = new InboundSourcePickerRowModel { IsChecked = true, OriginalQty = 100, RemainingReceivableQty = 30 };
        var vm = new InboundSourcePickerViewModel();
        vm.LoadItems([first, new InboundSourcePickerRowModel()]);
        Assert.AreEqual(1, vm.GetCheckedItems().Count);
        vm.ToggleAllCommand.Execute(null);
        Assert.AreEqual(2, vm.GetCheckedItems().Count);
        vm.ToggleAllCommand.Execute(null);
        Assert.AreEqual(0, vm.GetCheckedItems().Count);
        Assert.AreEqual(100m, first.OriginalQty);
        Assert.AreEqual(30m, first.RemainingReceivableQty);
        vm.LoadItems([]);
        vm.ToggleAllCommand.Execute(null);
        Assert.AreEqual(0, vm.GetCheckedItems().Count);
    }

    /// <summary>真实模板的复选框回写行模型，运行时不自动注入设计时数据。</summary>
    [TestMethod]
    public void View_CheckboxBinding_WritesSelection()
    {
        StaTest.RunAsync(() =>
        {
            var view = new InboundSourcePickerView();
            Assert.IsNull(view.DataContext);
            var grid = ((Grid)view.Content).Children.OfType<DataGrid>().Single();
            var column = (DataGridTemplateColumn)grid.Columns[0];
            var checkBox = (CheckBox)column.CellTemplate.LoadContent();
            var row = new InboundSourcePickerRowModel();
            checkBox.DataContext = row;
            checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateTarget();
            checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Assert.IsTrue(row.IsChecked);
            return Task.CompletedTask;
        });
    }
}
