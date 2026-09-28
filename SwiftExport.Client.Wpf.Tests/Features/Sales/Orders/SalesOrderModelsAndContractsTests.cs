using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Sales.Orders.Models;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderModelsAndContractsTests
{
    /// <summary>详情单位列只允许非标行编辑，并由 Unit 字典提供代码选项。</summary>
    [TestMethod]
    public void DetailUnitColumn_ShouldUseDictionaryForNonStandardLines()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "SwiftExport.Client.Wpf",
            "Features", "Sales", "Orders", "Views", "SalesOrderDetailView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var column = document.Descendants(presentation + "DataGridTemplateColumn")
            .Single(element => (string?)element.Attribute("Header") == "单位");
        var comboBox = column.Descendants(presentation + "ComboBox").Single();

        StringAssert.Contains((string?)comboBox.Attribute("ItemsSource"), "UnitOptions");
        StringAssert.Contains((string?)comboBox.Attribute("SelectedValue"), "UnitSnapshot");
        Assert.AreEqual("{Binding IsNonStandard}", (string?)comboBox.Attribute("IsEnabled"));
    }

    [TestMethod]
    public void DetailHeader_ShouldUseCachedDictionaryAndCurrencyComboBoxes()
    {
        var document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Sales",
            "Orders",
            "Views",
            "SalesOrderDetailView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Header.ShippedBy"] = "ShippedByOptions",
            ["Header.Currency"] = "CurrencyOptions",
            ["Header.PaymentMethod"] = "PaymentMethodOptions",
            ["Header.Incoterm"] = "IncotermOptions",
        };

        foreach (var (bindingPath, optionsProperty) in expected)
        {
            var comboBox = document.Descendants(presentation + "ComboBox").Single(element =>
                ((string?)element.Attribute("Text"))?.Contains(bindingPath, StringComparison.Ordinal) == true);
            StringAssert.Contains((string?)comboBox.Attribute("ItemsSource"), optionsProperty);
            StringAssert.Contains((string?)comboBox.Attribute("Text"), "Mode=TwoWay");
            Assert.AreEqual("True", (string?)comboBox.Attribute("IsEditable"));
            Assert.AreEqual("True", (string?)comboBox.Attribute("IsReadOnly"));
        }
    }

    [TestMethod]
    public void EditableReadOnlyComboBox_ShouldPreserveLegacyTextUntilUserSelectsOption()
    {
        StaTest.RunAsync(async () =>
        {
            var header = new SalesOrderHeaderEditModel { PaymentMethod = "LEGACY" };
            var comboBox = new ComboBox
            {
                DataContext = header,
                IsEditable = true,
                IsReadOnly = true,
                ItemsSource = new[]
                {
                    new SysDictDataDto { DataNameEn = "TT", DataNameZh = "电汇", IsEnabled = true },
                    new SysDictDataDto { DataNameEn = "LC", DataNameZh = "信用证", IsEnabled = true },
                },
            };
            TextSearch.SetTextPath(comboBox, nameof(SysDictDataDto.DataNameEn));
            comboBox.SetBinding(
                ComboBox.TextProperty,
                new Binding(nameof(SalesOrderHeaderEditModel.PaymentMethod))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                });
            var window = new Window { Content = comboBox, ShowInTaskbar = false, Width = 240, Height = 80 };

            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                Assert.AreEqual("LEGACY", header.PaymentMethod);
                Assert.AreEqual("LEGACY", comboBox.Text);

                comboBox.SelectedItem = comboBox.Items[0];
                await Dispatcher.Yield(DispatcherPriority.DataBind);

                Assert.AreEqual("TT", header.PaymentMethod);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void NonStandardSku_ShouldBeStableUppercaseAndUseSharedPrefix()
    {
        var uid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        var sku = NonStandardSkuGenerator.Create(uid);

        Assert.AreEqual("NS-0123456789AB", sku);
        Assert.AreEqual(sku, NonStandardSkuGenerator.Create(uid));
        Assert.IsTrue(NonStandardSkuGenerator.IsGeneratedNonStandardSku(sku));
        Assert.IsFalse(NonStandardSkuGenerator.IsGeneratedNonStandardSku("非标件-anything"));
        Assert.IsFalse(NonStandardSkuGenerator.IsGeneratedNonStandardSku("NS-0123456789ab"));
        Assert.ThrowsExactly<ArgumentException>(() => NonStandardSkuGenerator.Create(Guid.Empty));
    }

    [TestMethod]
    public void AddNonStandardLine_ShouldNotRequireProductAndShouldValidateBusinessSnapshot()
    {
        var list = new SalesOrderLineListModel();
        var deliveryDate = new DateTime(2026, 8, 1);

        var line = list.AddNonStandardLine(deliveryDate);

        Assert.IsTrue(line.IsNonStandard);
        Assert.IsNull(line.ProductId);
        Assert.AreEqual(line.ExpectedDeliveryDate, (DateTime?)deliveryDate);
        Assert.AreEqual(NonStandardSkuGenerator.Create(line.Uid), line.Sku);
        Assert.AreEqual("数量必须大于零。", line.ValidateRow());
        Assert.AreEqual("数量必须大于零。", line[nameof(SalesOrderLineModel.QtyText)]);

        line.Qty = 12;
        Assert.AreEqual("非标行必须填写产品描述。", line.ValidateRow());
        Assert.AreEqual("非标行必须填写产品描述。", line[nameof(SalesOrderLineModel.DescriptionEn)]);
        line.DescriptionEn = "Temporary custom brass component";
        Assert.AreEqual("非标行必须填写单位。", line.ValidateRow());
        line.UnitSnapshot = "PCS";
        Assert.IsNull(line.ValidateRow());
    }

    [TestMethod]
    public void StandardLine_ShouldRequireProductLookup()
    {
        var line = new SalesOrderLineListModel().AddStandardLine(null);
        line.Qty = 1;

        Assert.AreEqual("标准行必须通过产品 Lookup 选择产品。", line.ValidateRow());

        var productId = Guid.NewGuid();
        var customerProductId = Guid.NewGuid();
        line.ApplyProduct(new SalesOrderProductLookupDto
        {
            ProductId = productId,
            CustomerProductId = customerProductId,
            Sku = "STD-001",
            DescriptionEn = "Standard product",
            FeaturesEn = "Standard product features",
        });

        Assert.IsNull(line.ValidateRow());
        Assert.AreEqual(productId, line.ProductId);
        Assert.AreEqual(customerProductId, line.CustomerProductId);
        Assert.AreEqual("Standard product", line.DescriptionEn);
        Assert.AreEqual("Standard product features", line.FeaturesEn);
    }

    [TestMethod]
    public void NonStandardSaveDto_ShouldRecalculateSkuAndClearProductAssociations()
    {
        var line = new SalesOrderLineListModel().AddNonStandardLine(null);
        line.Qty = 2;
        line.DescriptionEn = "Custom item";
        line.UnitSnapshot = "PCS";
        line.ProductId = Guid.NewGuid();
        line.CustomerProductId = Guid.NewGuid();
        line.Sku = "client-tampered";

        var dto = line.ToEditDto();

        Assert.IsNull(dto.ProductId);
        Assert.IsNull(dto.CustomerProductId);
        Assert.AreEqual(NonStandardSkuGenerator.Create(line.Uid), dto.Sku);
        Assert.AreEqual("PCS", dto.UnitSnapshot);
    }

    [TestMethod]
    public void ManagerAndDetailContracts_ShouldNotExposeOldSalesOrderOrApprovalStatus()
    {
        Assert.IsNull(typeof(SalesOrderEditHeaderDto).GetProperty("OrderStatus"));
        Assert.IsNull(typeof(SalesOrderEditHeaderDto).GetProperty("ApprovalStatus"));
        Assert.IsNull(typeof(SalesOrderManagerListItemDto).GetProperty("OrderStatus"));
        Assert.IsNull(typeof(SalesOrderEditLineDto).GetProperty("CreatedDate"));
        Assert.IsNull(typeof(SalesOrderEditLineDto).GetProperty("UpdatedBy"));
        Assert.IsNull(typeof(SalesOrderEditLineDto).GetProperty("IsDeleted"));
        Assert.IsNotNull(typeof(ISalesOrderDetailApi).GetMethod(nameof(ISalesOrderDetailApi.ConfirmAsync)));
        Assert.IsNull(typeof(SalesOrderManagerSearchRequest).GetProperty("CustomerCode"));
        Assert.AreEqual(typeof(List<string>), typeof(SalesOrderManagerSearchRequest).GetProperty(nameof(SalesOrderManagerSearchRequest.CustomerCodes))!.PropertyType);
        var customerCodeMethod = typeof(ISalesOrderManagerApi).GetMethod(nameof(ISalesOrderManagerApi.GetCustomerCodesAsync));
        Assert.IsNotNull(customerCodeMethod);
        Assert.AreEqual(
            "/customer-codes",
            customerCodeMethod.GetCustomAttributes(typeof(GetAttribute), false).Cast<GetAttribute>().Single().Path);

        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(SalesOrderManagerViewModel.RouteKey, out var manager));
        Assert.AreEqual(typeof(SalesOrderManagerViewModel), manager.ViewModelType);
        Assert.IsTrue(registry.TryGetRoute(SalesOrderManagerViewModel.DetailRouteKey, out var detail));
        Assert.AreEqual(typeof(SalesOrderDetailViewModel), detail.ViewModelType);
    }

    [TestMethod]
    public void NumericSoftEditorText_ShouldPreserveInvalidInputAndLastValidStrongValue()
    {
        var line = new SalesOrderLineModel();
        line.Load(new SalesOrderDetailLineDto
        {
            Uid = Guid.NewGuid(),
            Qty = 12.5m,
            CnyPrice = 8.2m,
            UsdPrice = 1.1m,
            ProductId = Guid.NewGuid(),
            Sku = "STD-001",
        });

        line.QtyText = "not-a-number";
        line.CnyPriceText = "bad-price";

        Assert.AreEqual(12.5m, line.Qty);
        Assert.AreEqual(8.2m, line.CnyPrice);
        Assert.AreEqual("not-a-number", line.QtyText);
        Assert.AreEqual("请输入有效数量。", line[nameof(SalesOrderLineModel.QtyText)]);
        Assert.AreEqual("请输入有效的 CNY 单价。", line[nameof(SalesOrderLineModel.CnyPriceText)]);
        Assert.AreEqual("请输入有效数量。", line.ValidateRow());

        line.QtyText = "20";
        line.CnyPriceText = string.Empty;

        Assert.AreEqual(20m, line.Qty);
        Assert.IsNull(line.CnyPrice);
        Assert.AreEqual(string.Empty, line[nameof(SalesOrderLineModel.QtyText)]);
        Assert.IsNull(line.ValidateRow());
    }

    [TestMethod]
    public void SoftEditorText_RejectChangesShouldRestoreCanonicalBaseline()
    {
        var line = new SalesOrderLineModel();
        line.Load(new SalesOrderDetailLineDto
        {
            Uid = Guid.NewGuid(),
            Qty = 2m,
            UsdPrice = 3.5m,
            ExpectedDeliveryDate = new DateTime(2026, 10, 2),
            ProductId = Guid.NewGuid(),
            Sku = "STD-002",
        });

        line.QtyText = "invalid";
        line.UsdPriceText = "invalid";
        line.ExpectedDeliveryDate = new DateTime(2027, 1, 3);
        line.RejectChanges();

        Assert.AreEqual("2", line.QtyText);
        Assert.AreEqual("3.5", line.UsdPriceText);
        Assert.IsTrue(line.ExpectedDeliveryDate == new DateTime(2026, 10, 2));
        Assert.IsNull(line.ValidateRow());
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
}
