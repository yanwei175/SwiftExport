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

/// <summary>验证产品页字段分组、基础资料模板及列可见性接线。</summary>
[TestClass]
public sealed class ProductsViewFieldModeTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>验证现行简单和复杂字段分组及复杂列的视图模型可见性绑定。</summary>
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
            "图片", "主产品型号", "物料编号", "产品型号", "中文描述", "中文配置", "颜色",
            "分类", "物料类型", "基础单位", "供货类型", "BOM", "默认供应商", "人民币价",
            "美元价", "备注",
        };
        foreach (var key in alwaysVisibleKeys)
        {
            var column = FindColumn(document, key);
            Assert.IsNull(column.Attribute("Visibility"), $"简单模式列 {key} 不应绑定复杂模式可见性。");
        }

        var complexKeys = new[]
        {
            "英文描述", "英文配置", "BOM版本", "供应商名称",
            "装箱数", "箱净重", "箱毛重", "箱体积",
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
    public void ProductUnitColumn_ShouldUseLocalDictionaryLookupAndBusinessFillField()
    {
        var column = FindColumn(LoadProductsView(), "基础单位");
        Assert.AreEqual("Unit", column.Attributes().Single(attribute =>
            attribute.Name.LocalName == "DataGridFillDownBehavior.Field").Value);

        var comboBox = column.Descendants(Presentation + "ComboBox").Single();
        StringAssert.Contains((string?)comboBox.Attribute("ItemsSource") ?? string.Empty, "UnitOptions");
        Assert.AreEqual("DataNameEn", (string?)comboBox.Attribute("SelectedValuePath"));
        StringAssert.Contains((string?)comboBox.Attribute("SelectedValue") ?? string.Empty, "Unit");
        StringAssert.Contains((string?)comboBox.Attribute("SelectedValue") ?? string.Empty, "Mode=TwoWay");
        Assert.IsFalse(column.Elements().Any(element => element.Name.LocalName == "DataGridTemplateColumn.CellEditingTemplate"));
    }

    /// <summary>验证图片列使用缓存展示，并编辑原图片引用字段。</summary>
    [TestMethod]
    public void MainImageColumn_ShouldUseCachedDisplayAndEditablePath()
    {
        var column = FindColumn(LoadProductsView(), "图片");
        Assert.IsFalse(column.Attributes().Any(attribute =>
            attribute.Name.LocalName == "DataGridFillDownBehavior.Field"));
        Assert.IsTrue(column.Descendants().Any(element => element.Name.LocalName == "CachedImage"));
        var editor = column.Descendants(Presentation + "TextBox").Single();
        StringAssert.Contains((string?)editor.Attribute("Text") ?? string.Empty, "MainImage");
        StringAssert.Contains((string?)editor.Attribute("Text") ?? string.Empty, "Mode=TwoWay");
    }

    /// <summary>验证颜色行编辑、分类树多选筛选及分类列只读展示；不恢复旧单选 Lookup。</summary>
    [TestMethod]
    public void ColorLookupAndReadonlyCategory_ShouldUseExpectedTemplates()
    {
        var document = LoadProductsView();
        var colorEditor = document.Descendants()
            .Single(element => element.Name.LocalName == "ProductColorLookupTextBox");
        StringAssert.Contains((string?)colorEditor.Attribute("ItemsSource") ?? string.Empty, "ReferenceDataCache.ProductColors");
        StringAssert.Contains((string?)colorEditor.Attribute("SelectedColor") ?? string.Empty, "Mode=OneWayToSource");

        var categoryFilter = document.Descendants()
            .Single(element => element.Name.LocalName == "ProductCategoryMutiSelectButton");
        StringAssert.Contains((string?)categoryFilter.Attribute("ItemsSource") ?? string.Empty,
            "ReferenceDataCache.ProductCategoryTree");
        Assert.AreEqual("{Binding FilterCategories, Mode=OneWay}", (string?)categoryFilter.Attribute("SelectedItems"));
        Assert.AreEqual("{Binding SearchCommand}", (string?)categoryFilter.Attribute("SelectionCommittedCommand"));

        var colorColumn = FindColumn(document, "颜色");
        var categoryColumn = FindColumn(document, "分类");
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
        .Descendants(Presentation + "DataGrid")
        .Single(grid => (string?)grid.Attribute("ItemsSource") == "{Binding Products.Items}")
        .Element(Presentation + "DataGrid.Columns")!.Elements()
        .Single(element => element.Name.LocalName.StartsWith("DataGrid", StringComparison.Ordinal)
                           && element.Name.LocalName.EndsWith("Column", StringComparison.Ordinal)
                           && element.Attributes().Any(attribute =>
                               attribute.Name.LocalName == "Header"
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
