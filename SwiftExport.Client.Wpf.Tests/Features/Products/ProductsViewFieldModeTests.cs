using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductsViewFieldModeTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void FieldModeToggleAndColumns_ShouldMatchSimpleAndComplexContract()
    {
        var document = LoadProductsView();
        var toggles = document.Descendants().Where(element => element.Name.LocalName == "ToggleSwitch").ToArray();
        Assert.IsTrue(toggles.Any(toggle =>
            (string?)toggle.Attribute("OffContent") == "简单字段"
            && (string?)toggle.Attribute("OnContent") == "复杂字段"
            && ((string?)toggle.Attribute("IsChecked"))?.Contains("IsComplexFieldMode", StringComparison.Ordinal) == true));

        var alwaysVisibleKeys = new[]
        {
            "main-image", "product-sku", "link-sku", "description-zh", "features-zh", "finish",
            "category-name", "material-type", "product-unit", "supply-type", "bom-no", "default-customer-code",
            "default-customer-sku", "default-supplier-code", "default-supplier-sku", "cny-price",
            "usd-price", "note",
        };
        foreach (var key in alwaysVisibleKeys)
        {
            var column = FindColumn(document, key);
            Assert.IsNull(column.Attribute("Visibility"), $"简单模式列 {key} 不应绑定复杂模式可见性。");
        }

        var complexKeys = new[]
        {
            "master-sku", "description-en", "features-en", "bom-version", "supplier-name",
            "pcs-of-carton", "nw-of-carton", "gw-of-carton", "cbm-of-carton",
        };
        foreach (var key in complexKeys)
        {
            var visibility = (string?)FindColumn(document, key).Attribute("Visibility");
            StringAssert.Contains(visibility ?? string.Empty, "IsComplexFieldMode");
            StringAssert.Contains(visibility ?? string.Empty, "StaticResource ProductsViewModelProxy");
        }

        Assert.IsFalse(document.Descendants().Attributes().Any(attribute =>
            attribute.Value.Contains("x:Reference Root", StringComparison.Ordinal)));

        Assert.IsFalse(document.Descendants().Any(element =>
            ((string?)element.Attribute("Command"))?.Contains("OpenCategoryManagerCommand", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void ProductUnitColumn_ShouldUseLocalDictionaryLookupAndStableFieldPath()
    {
        var column = FindColumn(LoadProductsView(), "product-unit");
        Assert.AreEqual("Unit", column.Attributes().Single(attribute =>
            attribute.Name.LocalName.EndsWith("FieldPath", StringComparison.Ordinal)).Value);

        var comboBox = column.Descendants(Presentation + "ComboBox").Single();
        StringAssert.Contains((string?)comboBox.Attribute("ItemsSource") ?? string.Empty, "UnitOptions");
        Assert.AreEqual("DataNameEn", (string?)comboBox.Attribute("SelectedValuePath"));
        StringAssert.Contains((string?)comboBox.Attribute("SelectedValue") ?? string.Empty, "Unit");
        StringAssert.Contains((string?)comboBox.Attribute("SelectedValue") ?? string.Empty, "Mode=TwoWay");
        Assert.IsFalse(column.Elements().Any(element => element.Name.LocalName == "DataGridTemplateColumn.CellEditingTemplate"));
    }

    [TestMethod]
    public void MainImageColumn_ShouldUseCachedDisplayAndEditablePath()
    {
        var column = FindColumn(LoadProductsView(), "main-image");
        Assert.AreEqual("MainImage", column.Attributes().Single(attribute =>
            attribute.Name.LocalName.EndsWith("FieldPath", StringComparison.Ordinal)).Value);
        Assert.IsTrue(column.Descendants().Any(element => element.Name.LocalName == "CachedImage"));
        var editor = column.Descendants(Presentation + "TextBox").Single();
        StringAssert.Contains((string?)editor.Attribute("Text") ?? string.Empty, "MainImage");
        StringAssert.Contains((string?)editor.Attribute("Text") ?? string.Empty, "Mode=TwoWay");
    }

    [TestMethod]
    public void ColorLookupAndReadonlyCategory_ShouldUseExpectedTemplates()
    {
        var document = LoadProductsView();
        var colorLookups = document.Descendants()
            .Where(element => element.Name.LocalName == "LookupTextBox"
                              && ((string?)element.Attribute("ItemsSource"))?.Contains(
                                  "ReferenceDataCache.ProductColors",
                                  StringComparison.Ordinal) == true)
            .ToArray();
        Assert.HasCount(1, colorLookups, "筛选区继续使用通用本地 Lookup。");
        Assert.IsTrue(colorLookups.All(element => (string?)element.Attribute("SearchMode") == "Local"));
        Assert.IsFalse(colorLookups.Any(element => element.Attribute("SearchCommand") is not null));

        var colorEditor = document.Descendants()
            .Single(element => element.Name.LocalName == "ProductColorLookupTextBox");
        StringAssert.Contains((string?)colorEditor.Attribute("ItemsSource") ?? string.Empty, "ReferenceDataCache.ProductColors");
        StringAssert.Contains((string?)colorEditor.Attribute("SelectedColor") ?? string.Empty, "Mode=OneWayToSource");

        var categoryTreeLookup = document.Descendants()
            .Single(element => element.Name.LocalName == "ProductCategoryLookupTextBox");
        StringAssert.Contains((string?)categoryTreeLookup.Attribute("ItemsSource") ?? string.Empty,
            "ReferenceDataCache.ProductCategoryTree");

        var colorColumn = FindColumn(document, "finish");
        var categoryColumn = FindColumn(document, "category-name");
        Assert.AreEqual("DataGridTemplateColumn", colorColumn.Name.LocalName);
        Assert.AreEqual("DataGridTemplateColumn", categoryColumn.Name.LocalName);
        Assert.IsFalse(colorColumn.Attributes().Any(attribute =>
            attribute.Name.LocalName.EndsWith("FieldPath", StringComparison.Ordinal)));
        Assert.IsFalse(categoryColumn.Attributes().Any(attribute =>
            attribute.Name.LocalName.EndsWith("FieldPath", StringComparison.Ordinal)));
        Assert.AreEqual("CategoryName", (string?)categoryColumn.Attribute("SortMemberPath"));
        Assert.AreEqual("CategoryName", (string?)categoryColumn.Descendants(Presentation + "TextBlock").Single().Attribute("Text")?.Value.Trim('{', '}').Split(',')[0].Replace("Binding ", string.Empty));
        Assert.IsFalse(categoryColumn.Elements().Any(element => element.Name.LocalName == "DataGridTemplateColumn.CellEditingTemplate"));
    }

    [TestMethod]
    public void BindingProxyColumnVisibility_ShouldLoadWithoutMarkupExtensionCycleAndTrackViewModel()
    {
        StaTest.RunAsync(() =>
        {
            const string xaml = """
                <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:resourceProxies="clr-namespace:SwiftExportClient.Resources.Proxies;assembly=SwiftExport.Client.Wpf">
                    <UserControl.Resources>
                        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
                        <resourceProxies:BindingProxy x:Key="ViewModelProxy" Data="{Binding}" />
                    </UserControl.Resources>
                    <DataGrid x:Name="Grid" AutoGenerateColumns="False">
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="复杂字段"
                                Visibility="{Binding Data.IsComplexFieldMode, Source={StaticResource ViewModelProxy}, Converter={StaticResource BooleanToVisibilityConverter}}" />
                        </DataGrid.Columns>
                    </DataGrid>
                </UserControl>
                """;

            var view = (UserControl)XamlReader.Parse(xaml);
            var source = new FieldModeSource();
            view.DataContext = source;
            PumpDispatcher();
            var grid = (DataGrid)view.FindName("Grid");
            Assert.AreEqual(Visibility.Collapsed, grid.Columns[0].Visibility);

            source.IsComplexFieldMode = true;
            PumpDispatcher();
            Assert.AreEqual(Visibility.Visible, grid.Columns[0].Visibility);
            return Task.CompletedTask;
        });
    }

    private static XElement FindColumn(XDocument document, string key) => document
        .Descendants()
        .Single(element => element.Name.LocalName.StartsWith("DataGrid", StringComparison.Ordinal)
                           && element.Name.LocalName.EndsWith("Column", StringComparison.Ordinal)
                           && element.Attributes().Any(attribute =>
                               attribute.Name.LocalName.EndsWith("Key", StringComparison.Ordinal)
                               && string.Equals(attribute.Value, key, StringComparison.Ordinal)));

    private static XDocument LoadProductsView() => XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "SwiftExport.Client.Wpf",
        "Features",
        "Products",
        "ProductList",
        "Views",
        "ProductsView.xaml"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class FieldModeSource : INotifyPropertyChanged
    {
        private bool _isComplexFieldMode;

        public bool IsComplexFieldMode
        {
            get => _isComplexFieldMode;
            set
            {
                if (_isComplexFieldMode == value) return;
                _isComplexFieldMode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsComplexFieldMode)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
