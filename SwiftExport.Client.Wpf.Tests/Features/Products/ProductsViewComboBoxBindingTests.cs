using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
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
    public void TemplateComboBoxColumns_RecreateAfterUnload_ShouldKeepCurrentEmptyAndLegacyValuesUnchanged()
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
    public void TemplateComboBox_ShouldWriteOnlyUserSelection()
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
            var editor = FindVisualChild<ComboBox>(cell);
            Assert.IsNotNull(editor);
            Assert.AreEqual("旧物料类型", list.Items[0].MaterialType);
            Assert.AreEqual(LineState.Unchanged, list.Items[0].State);

            editor.SelectedItem = MaterialTypesEnum.零件;
            PumpDispatcher();

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
        grid.Columns.Add(CreateTemplateColumn(
            "物料类型",
            MaterialTypesEnum.MaterialTypes,
            nameof(ProductLineModel.MaterialType)));

        if (includeSupplyType)
        {
            grid.Columns.Add(CreateTemplateColumn(
                "供货类型",
                SupplyTypeEnums.SupplyTypes,
                nameof(ProductLineModel.SupplyType)));
        }

        return grid;
    }

    private static DataGridTemplateColumn CreateTemplateColumn(
        string header,
        IEnumerable<string> itemsSource,
        string propertyName)
    {
#pragma warning disable CS0618
        var factory = new FrameworkElementFactory(typeof(ComboBox));
        factory.SetValue(ItemsControl.ItemsSourceProperty, itemsSource);
        factory.SetBinding(
            System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
            new Binding(propertyName)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        var template = new DataTemplate { VisualTree = factory };
#pragma warning restore CS0618
        return new DataGridTemplateColumn { Header = header, CellTemplate = template };
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
