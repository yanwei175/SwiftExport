using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
[DoNotParallelize]
public sealed class ProductsViewComboBoxBindingTests
{
    [TestMethod]
    public void ComboBoxColumns_RecreateAfterUnload_ShouldKeepCurrentEmptyAndLegacyValuesUnchanged()
    {
        StaTest.RunAsync(() =>
        {
            var list = new ProductListModel();
            list.LoadFromDtos(
            [
                new ProductDto { Uid = Guid.NewGuid(), MaterialType = MaterialTypesEnum.成品, SupplyType = SupplyTypeEnums.采购 },
                new ProductDto { Uid = Guid.NewGuid(), MaterialType = string.Empty, SupplyType = string.Empty },
                new ProductDto { Uid = Guid.NewGuid(), MaterialType = "旧物料类型", SupplyType = "旧供货类型" },
            ]);

            var window = new Window { Width = 500, Height = 240, ShowInTaskbar = false };
            window.Content = CreateGrid(list);
            window.Show();
            PumpDispatcher();

            window.Content = new Border();
            PumpDispatcher();

            window.Content = CreateGrid(list);
            PumpDispatcher();

            Assert.AreEqual(MaterialTypesEnum.成品, list.Items[0].MaterialType);
            Assert.AreEqual(SupplyTypeEnums.采购, list.Items[0].SupplyType);
            Assert.AreEqual(string.Empty, list.Items[1].MaterialType);
            Assert.AreEqual(string.Empty, list.Items[1].SupplyType);
            Assert.AreEqual("旧物料类型", list.Items[2].MaterialType);
            Assert.AreEqual("旧供货类型", list.Items[2].SupplyType);
            Assert.IsTrue(list.Items.All(line => line.State == LineState.Unchanged));

            window.Close();

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void TextBinding_ShouldDisplayLegacyValueAndWriteOnlyUserSelection()
    {
        StaTest.RunAsync(() =>
        {
            var list = new ProductListModel();
            list.LoadFromDtos([new ProductDto { Uid = Guid.NewGuid(), MaterialType = "旧物料类型" }]);
            var grid = CreateGrid(list, includeSupplyType: false);
            var window = new Window { Content = grid, Width = 320, Height = 160, ShowInTaskbar = false };
            window.Show();
            PumpDispatcher();

            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var cell = GetCell(grid, row, 0);
            var display = cell.Content as TextBlock ?? FindVisualChild<TextBlock>(cell);
            Assert.AreEqual(
                "旧物料类型",
                display?.Text,
                $"Content={cell.Content?.GetType().FullName}, Source={list.Items[0].MaterialType}, Binding={(display is null ? "none" : BindingOperations.GetBindingExpression(display, TextBlock.TextProperty)?.Status.ToString())}");
            Assert.AreEqual(LineState.Unchanged, list.Items[0].State);

            grid.CurrentCell = new DataGridCellInfo(list.Items[0], grid.Columns[0]);
            grid.SelectedCells.Clear();
            grid.SelectedCells.Add(grid.CurrentCell);
            Assert.IsTrue(grid.BeginEdit());
            PumpDispatcher();

            var editor = FindVisualChild<ComboBox>(cell);
            Assert.IsNotNull(editor);
            editor.SelectedItem = MaterialTypesEnum.零件;
            PumpDispatcher();
            grid.CommitEdit(DataGridEditingUnit.Cell, true);

            Assert.AreEqual(MaterialTypesEnum.零件, list.Items[0].MaterialType);
            Assert.AreEqual(LineState.Modified, list.Items[0].State);

            window.Close();
            return Task.CompletedTask;
        });
    }

    private static DataGrid CreateGrid(ProductListModel list, bool includeSupplyType = true)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = list.Items,
            SelectionUnit = DataGridSelectionUnit.Cell,
        };
        var materialDisplayStyle = CreateDisplayStyle(nameof(ProductLineModel.MaterialType));
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "物料类型",
            ItemsSource = MaterialTypesEnum.MaterialTypes,
            TextBinding = new Binding(nameof(ProductLineModel.MaterialType))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            },
            ElementStyle = materialDisplayStyle,
        });

        if (includeSupplyType)
        {
            var supplyDisplayStyle = CreateDisplayStyle(nameof(ProductLineModel.SupplyType));
            grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "供货类型",
                ItemsSource = SupplyTypeEnums.SupplyTypes,
                TextBinding = new Binding(nameof(ProductLineModel.SupplyType))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                },
                ElementStyle = supplyDisplayStyle,
            });
        }

        return grid;
    }

    private static Style CreateDisplayStyle(string propertyName)
    {
        var template = (ControlTemplate)XamlReader.Parse(
            $"<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ComboBox\"><TextBlock Text=\"{{Binding {propertyName}}}\" /></ControlTemplate>");
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static DataGridCell GetCell(DataGrid grid, DataGridRow row, int columnIndex)
    {
        grid.ScrollIntoView(row.Item, grid.Columns[columnIndex]);
        PumpDispatcher();
        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        Assert.IsNotNull(presenter);
        return (DataGridCell)presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex);
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void PumpDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

}
