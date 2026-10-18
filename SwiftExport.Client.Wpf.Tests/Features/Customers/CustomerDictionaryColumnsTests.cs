using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证客户字典候选接线、真实模板生命周期与选择回写。</summary>
[TestClass]
public sealed class CustomerDictionaryColumnsTests
{
    /// <summary>真实币种模板重建保留当前、空和历史值，主动选择才标记业务变更。</summary>
    [TestMethod]
    public void TemplateComboBox_RecreateAfterUnload_ShouldNotOverwriteCurrentOrLegacyValues()
    {
        StaTest.RunAsync(() =>
        {
            var list = new CustomerListModel();
            list.LoadFromDtos(
            [
                new CustomerDto { Uid = Guid.NewGuid(), Currency = "USD" },
                new CustomerDto { Uid = Guid.NewGuid(), Currency = string.Empty },
                new CustomerDto { Uid = Guid.NewGuid(), Currency = "LEGACY" },
            ]);
            var options = new[]
            {
                new SysDicCurrencyDto { Uid = Guid.Empty, CurrencyCode = string.Empty, CurrencyNameZh = "（空）", IsEnabled = true },
                Currency("USD")
            };
            var window = new Window { Width = 320, Height = 200, ShowInTaskbar = false };

            window.Content = CreateDictionaryGrid(list, options);
            window.Show();
            PumpDispatcher();
            window.Content = new Border();
            PumpDispatcher();
            window.Content = CreateDictionaryGrid(list, options);
            PumpDispatcher();

            CollectionAssert.AreEqual(
                new[] { "USD", string.Empty, "LEGACY" },
                list.Items.Select(item => item.Currency).ToArray());
            Assert.IsTrue(list.Items.All(item => item.State == LineState.Unchanged));

            var grid = (DataGrid)window.Content;
            var firstRow = (DataGridRow?)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var comboBox = FindVisualChild<ComboBox>(firstRow);
            Assert.IsNotNull(comboBox);
            comboBox.SelectedValue = string.Empty;
            PumpDispatcher();

            Assert.AreSame(options[0], comboBox.SelectedItem);
            Assert.AreEqual(string.Empty, list.Items[0].Currency);
            Assert.AreEqual(LineState.Modified, list.Items[0].State);
            window.Close();
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DictionaryCacheChanges_ShouldRefreshCustomerColumnOptions()
    {
        StaTest.RunAsync(() =>
        {
            var cache = new ReferenceDataCacheStub(
            [
                DictionaryValue(DictCodes.PaymentMethod, "TT"),
                DictionaryValue(DictCodes.Incoterm, "FOB"),
                DictionaryValue(DictCodes.ShippedBy, "SEA"),
                DictionaryValue(DictCodes.IncotermLocation, "Shanghai"),
                DictionaryValue(DictCodes.IncotermSortRemark, "FreightPrepaid"),
                DictionaryValue(DictCodes.PaymentMethod, "Disabled", isEnabled: false),
            ], [Currency("USD")]);
            var viewModel = new CustomerManagerViewModel(
                null!,
                null!,
                null!,
                null!,
                cache);

            Assert.IsNotEmpty(viewModel.CurrencyOptions);
            Assert.AreEqual(Guid.Empty, viewModel.CurrencyOptions[0].Uid);
            Assert.AreEqual(string.Empty, viewModel.CurrencyOptions[0].CurrencyCode);
            Assert.AreEqual("（空）", viewModel.CurrencyOptions[0].CurrencyNameZh);
            AssertEmptyOption(viewModel.PaymentMethodOptions);
            AssertEmptyOption(viewModel.IncotermOptions);
            AssertEmptyOption(viewModel.ShippedByOptions);
            AssertEmptyOption(viewModel.IncotermLocationOptions);
            AssertEmptyOption(viewModel.IncotermSortRemarkOptions);
            Assert.AreEqual("USD", viewModel.CurrencyOptions[1].CurrencyCode);
            Assert.AreEqual("TT", viewModel.PaymentMethodOptions[1].DataNameEn);
            Assert.AreEqual("FOB", viewModel.IncotermOptions[1].DataNameEn);
            Assert.AreEqual("SEA", viewModel.ShippedByOptions[1].DataNameEn);
            Assert.AreEqual("Shanghai", viewModel.IncotermLocationOptions[1].DataNameEn);
            Assert.AreEqual("FreightPrepaid", viewModel.IncotermSortRemarkOptions[1].DataNameEn);

            cache.Add(DictionaryValue(DictCodes.PaymentMethod, "LC"));

            CollectionAssert.AreEqual(
                new[] { string.Empty, "TT", "LC" },
                viewModel.PaymentMethodOptions.Select(item => item.DataNameEn).ToArray());
            return Task.CompletedTask;
        });
    }

    /// <summary>六个字典字段在单元格内直接选择，保留各自候选源、代码路径和双向绑定。</summary>
    [TestMethod]
    public void CustomerDictionaryFields_ShouldUseTemplateComboBoxes()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Customers",
            "CustomerManagement",
            "Views",
            "CustomerManagerView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Incoterm"] = "IncotermOptions",
            ["IncotermLocation"] = "IncotermLocationOptions",
            ["IncotermSortRemark"] = "IncotermSortRemarkOptions",
            ["PaymentMethod"] = "PaymentMethodOptions",
            ["Currency"] = "CurrencyOptions",
            ["ShippedBy"] = "ShippedByOptions",
        };

        Assert.IsFalse(document.Descendants(presentation + "DataGridComboBoxColumn").Any());
        foreach (var (fieldPath, optionsProperty) in expected)
        {
            var column = document.Descendants(presentation + "DataGridTemplateColumn")
                .Single(element => element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "SortMemberPath"
                    && string.Equals(attribute.Value, fieldPath, StringComparison.Ordinal)));
            Assert.AreEqual(fieldPath, (string?)column.Attribute("SortMemberPath"));

            var comboBox = column.Descendants(presentation + "ComboBox").SingleOrDefault();
            Assert.IsNotNull(comboBox);
            Assert.AreEqual(
                fieldPath == "Currency" ? "CurrencyCode" : "DataNameEn",
                (string?)comboBox.Attribute("SelectedValuePath"));
            StringAssert.Contains((string?)comboBox.Attribute("ItemsSource"), optionsProperty);
            StringAssert.Contains((string?)comboBox.Attribute("SelectedValue"), fieldPath);
            StringAssert.Contains((string?)comboBox.Attribute("SelectedValue"), "Mode=TwoWay");

            // 当前字典列直接在单元格中选择，不要求恢复独立 TextBlock 显示态。
            var cellTemplate = column.Element(presentation + "DataGridTemplateColumn.CellTemplate");
            Assert.IsNotNull(cellTemplate);
            Assert.AreSame(comboBox, cellTemplate.Descendants(presentation + "ComboBox").Single());
            Assert.IsNull(column.Element(presentation + "DataGridTemplateColumn.CellEditingTemplate"));
        }
    }

    [TestMethod]
    public void CustomerManagerSelectionMode_ShouldDefaultToFullRowAndBindSelectedItem()
    {
        var cache = new ReferenceDataCacheStub([]);
        var customerProducts = new CustomerProductManagerViewModel(
            null!, null!, null!, null!, null!, new SwiftExport.Client.Wpf.Tests.TestInfrastructure.ReferenceDataCacheServiceStub());
        var viewModel = new CustomerManagerViewModel(
            null!,
            null!,
            null!,
            null!,
            cache);
        Assert.IsFalse(viewModel.IsCellSelectionMode);
        viewModel.IsCellSelectionMode = true;
        Assert.IsTrue(viewModel.IsCellSelectionMode);
        customerProducts.IsOpen = true;
        viewModel.IsCellSelectionMode = false;
        Assert.IsFalse(viewModel.IsCellSelectionMode, "独立产品编辑器不再控制客户页选择模式。");

        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Customers",
            "CustomerManagement",
            "Views",
            "CustomerManagerView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dataGrid = document.Descendants().Single(element => element.Name == presentation + "DataGrid"
            || element.Name == XName.Get("DataGrid", "http://schemas.lepo.co/wpfui/2022/xaml"));

        Assert.AreEqual("Extended", (string?)dataGrid.Attribute("SelectionMode"));
        StringAssert.Contains((string?)dataGrid.Attribute("SelectionUnit"), "IsCellSelectionMode");
        StringAssert.Contains((string?)dataGrid.Attribute("SelectionUnit"), "GridSelectionUnitConverter");
        Assert.IsNull(dataGrid.Attribute("CurrentItem"));

        var currentRowBinding = dataGrid.Attributes().Single(attribute =>
            attribute.Name.LocalName == "SelectedItem");
        StringAssert.Contains(currentRowBinding.Value, "SelectedLine");
        StringAssert.Contains(currentRowBinding.Value, "Mode=TwoWay");

        var toggle = document.Descendants()
            .Single(element => element.Name.LocalName == "ToggleSwitch"
                && string.Equals((string?)element.Attribute("OnContent"), "格子模式", StringComparison.Ordinal));
        Assert.AreEqual("整行模式", (string?)toggle.Attribute("OffContent"));
        StringAssert.Contains((string?)toggle.Attribute("IsChecked"), "IsCellSelectionMode");
        Assert.IsNull(toggle.Attribute("IsEnabled"));
        Assert.IsFalse(document.Descendants().Any(element => element.Name.LocalName == "RightDrawerHost"));
    }

    [TestMethod]
    public void CustomerProductColumns_ShouldNotExposeUnitOrCurrency()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Customers",
            "CustomerProducts",
            "Views",
            "CustomerProductManagerView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.IsFalse(document.ToString().Contains("CustomerSkuUnit", StringComparison.Ordinal));
        Assert.IsFalse(document.ToString().Contains("UnitOptions", StringComparison.Ordinal));
        Assert.IsFalse(document.Descendants()
            .Any(element => string.Equals((string?)element.Attribute("Header"), "单位", StringComparison.Ordinal)));
        Assert.IsFalse(document.Descendants()
            .Any(element => string.Equals((string?)element.Attribute("Header"), "币种", StringComparison.Ordinal)));

        var dataGrid = document.Descendants(presentation + "DataGrid").Single();
        Assert.AreEqual("Extended", (string?)dataGrid.Attribute("SelectionMode"));
        StringAssert.Contains((string?)dataGrid.Attribute("SelectionUnit"), "IsCellSelectionMode");
        StringAssert.Contains((string?)dataGrid.Attribute("SelectionUnit"), "GridSelectionUnitConverter");
        Assert.IsNull(dataGrid.Attribute("SelectedItem"));
        var currentRowBinding = dataGrid.Attributes().Single(attribute =>
            attribute.Name.LocalName == "CurrentItem");
        StringAssert.Contains(currentRowBinding.Value, "SelectedLine");
        StringAssert.Contains(currentRowBinding.Value, "Mode=TwoWay");

        var toggle = document.Descendants()
            .Single(element => element.Name.LocalName == "ToggleSwitch"
                && string.Equals((string?)element.Attribute("OnContent"), "格子模式", StringComparison.Ordinal));
        Assert.AreEqual("整行模式", (string?)toggle.Attribute("OffContent"));
        StringAssert.Contains((string?)toggle.Attribute("IsChecked"), "IsCellSelectionMode");
    }

    private static SysDictDataDto DictionaryValue(string code, string value, bool isEnabled = true) => new()
    {
        Uid = Guid.NewGuid(),
        DictCode = code,
        DataNameEn = value,
        DataNameZh = value,
        IsEnabled = isEnabled,
    };

    private static void AssertEmptyOption(IReadOnlyList<SysDictDataDto> options)
    {
        Assert.IsNotEmpty(options);
        Assert.AreEqual(Guid.Empty, options[0].Uid);
        Assert.AreEqual(string.Empty, options[0].DataNameEn);
        Assert.AreEqual("（空）", options[0].DataNameZh);
    }

    private static SysDicCurrencyDto Currency(string code) => new()
    {
        Uid = Guid.NewGuid(),
        CurrencyCode = code,
        CurrencyNameEn = code,
        CurrencyNameZh = code,
        IsEnabled = true,
    };

    /// <summary>加载客户页真实币种模板与资源，验证卸载重建不会改写历史值。</summary>
    private static DataGrid CreateDictionaryGrid(
        CustomerListModel list,
        IReadOnlyList<SysDicCurrencyDto> options)
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf",
            "Features", "Customers", "CustomerManagement", "Views", "CustomerManagerView.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var sourceColumn = document.Descendants(p + "DataGridTemplateColumn")
            .Single(column => (string?)column.Attribute("SortMemberPath") == "Currency");
        var template = document.Descendants(p + "DataTemplate")
            .Single(element => (string?)element.Attribute(x + "Key") == "CustomerCurrencyOptionTemplate");
        var markup = new XElement(p + "DataGrid", new XAttribute("xmlns", p.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName), new XAttribute("AutoGenerateColumns", "False"),
            new XElement(p + "DataGrid.Resources", new XElement(template)),
            new XElement(p + "DataGrid.Columns", new XElement(p + "DataGridTemplateColumn",
                new XAttribute("Header", "币种"), new XElement(sourceColumn.Element(p + "DataGridTemplateColumn.CellTemplate")!))));
        var grid = (DataGrid)System.Windows.Markup.XamlReader.Parse(markup.ToString());
        grid.DataContext = new { CurrencyOptions = options };
        grid.ItemsSource = list.Items;
        return grid;
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static T? FindVisualChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    /// <summary>为客户页面测试提供离线字典缓存，不连接业务服务。</summary>
    internal sealed class ReferenceDataCacheStub : IReferenceDataCacheService
    {
        private readonly ObservableCollection<SysDictDataDto> _dictionaryValues;
        private readonly ObservableCollection<SysDicCurrencyDto> _currencies;

        public ReferenceDataCacheStub(
            IEnumerable<SysDictDataDto> dictionaryValues,
            IEnumerable<SysDicCurrencyDto>? currencies = null)
        {
            _dictionaryValues = new ObservableCollection<SysDictDataDto>(dictionaryValues);
            _currencies = new ObservableCollection<SysDicCurrencyDto>(currencies ?? []);
            DictionaryValues = new ReadOnlyObservableCollection<SysDictDataDto>(_dictionaryValues);
            Currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>(_currencies);
            Customers = new ReadOnlyObservableCollection<CustomerDto>([]);
            Suppliers = new ReadOnlyObservableCollection<SupplierDto>([]);
            WarehouseSites = new ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto>([]);
            ProductColors = new ReadOnlyObservableCollection<ProductColorDto>([]);
            ProductCategoryTree = new ReadOnlyObservableCollection<ProductCategoryCacheNode>([]);
            ProductCategories = new ReadOnlyObservableCollection<ProductCategoryCacheNode>([]);
            DictionaryTypes = new ReadOnlyObservableCollection<SysDictTypeDto>([]);
        }

        public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; }
        public ReadOnlyObservableCollection<CustomerDto> Customers { get; }
        public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; }
        public ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto> WarehouseSites { get; }
        public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; }
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; }
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; }
        /// <inheritdoc />
        public ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto> ProductAttributeGroups { get; } = new([]);
        /// <inheritdoc />
        public ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeProfileGroupDto> ProductAttributeProfileRelations { get; } = new([]);
        /// <inheritdoc />
        public ProductAttributeResolution ResolveProductAttributes(Guid categoryId) => new(false, "测试未提供标签定义。", [], []);
        /// <inheritdoc />
        public bool AreProductAttributesLoaded => true;
        /// <inheritdoc />
        public event EventHandler? ProductAttributesChanged { add { } remove { } }
        /// <inheritdoc />
        public IReadOnlyList<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto> GetProductAttributeGroups(Guid categoryId) => [];
        /// <inheritdoc />
        public IReadOnlyList<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeOptionDto> GetProductAttributeOptions(Guid groupId) => [];
        /// <inheritdoc />
        public Task<CacheRefreshResult> EnsureProductAttributesLoadedAsync() => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<CacheRefreshResult> RefreshProductAttributesAsync() => throw new NotSupportedException();
        public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; }
        public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; }

        public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true) =>
            DictionaryValues
                .Where(item => string.Equals(item.DictCode, dictCode, StringComparison.OrdinalIgnoreCase)
                    && (!enabledOnly || item.IsEnabled))
                .ToArray();

        public void Add(SysDictDataDto item) => _dictionaryValues.Add(item);

        public Task<CacheRefreshResult> EnsureLoadedAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshAllAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshProductColorsAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshCustomersAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshSuppliersAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshWarehouseSitesAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshProductCategoriesAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshCurrenciesAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshDictionariesAsync() => throw new NotSupportedException();
        public void Clear()
        {
            _dictionaryValues.Clear();
            _currencies.Clear();
        }
    }
}
