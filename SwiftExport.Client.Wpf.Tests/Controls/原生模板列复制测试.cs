using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerManagement.Views;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Views;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>加载实际页面列并调用 WPF 原生复制取值，验证业务文本、勾选和关联 ID 的边界。</summary>
[TestClass]
public sealed class NativeTemplateClipboardTests
{
    /// <summary>物料模板复制可见编码和名称，不复制关联 ID，也不回写模型。</summary>
    [TestMethod]
    public void ProductTemplates_ShouldCopyDisplayFieldsWithoutChangingIdentity() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var supplierId = Guid.NewGuid();
            var row = new ProductLineModel
            {
                DefaultSupplierId = supplierId, DefaultSupplierCode = "SUP-001", SupplierName = "供应商一",
                MasterSku = "MASTER-001", LinkSku = "SKU-001", Finish = "哑黑",
                ManufacturerCode = "MAKER-001", DefaultWarehouseCode = "WH-001"
            };
            await Verify(new ProductsView(), row, new Dictionary<string, object>
            {
                ["默认供应商"] = "SUP-001", ["主产品型号"] = "MASTER-001", ["产品型号"] = "SKU-001",
                ["颜色"] = "哑黑", ["制造商"] = "MAKER-001", ["产品默认仓库（参考）"] = "WH-001"
            });
            Assert.AreEqual(supplierId, row.DefaultSupplierId);
            Assert.AreEqual("供应商一", row.SupplierName);
        });

    /// <summary>采购候选列复制业务展示值和日期，选中标记仍是独立的业务值。</summary>
    [TestMethod]
    public void PurchaseTemplates_ShouldCopySupplierDisplayAndDate() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var row = new PurchaseCandidateItemModel
            {
                SupplierId = Guid.NewGuid(), PurchaseSupplierCode = "SUP-002", PurchaseSupplierName = "供应商二",
                PlannedDeliveryDate = new DateTime(2026, 10, 20), IsChecked = true
            };
            var supplierId = row.SupplierId;
            await Verify(new PurchaseCandidateSelectionView(), row, new Dictionary<string, object>
            {
                ["供应商"] = row.PurchaseSupplierDisplay, ["计划交期"] = row.PlannedDeliveryDate!,
                ["验证结果"] = row.ValidationDisplayMessage
            });
            Assert.AreEqual(supplierId, row.SupplierId);
            Assert.IsTrue(row.IsChecked);
        });

    /// <summary>客户端字典模板列通过原生剪贴板绑定复制值，不依赖 ComboBox 的可视实例。</summary>
    [TestMethod]
    public void CustomerTemplates_ShouldCopyDictionaryValues() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var row = new CustomerLineModel { Currency = "USD", PaymentMethod = "TT", Incoterm = "FOB" };
            await Verify(new CustomerManagerView(), row, new Dictionary<string, object>
            {
                ["币种"] = "USD", ["付款方式"] = "TT", ["贸易条款"] = "FOB"
            });
            Assert.AreEqual("USD", row.Currency);
        });

    /// <summary>只使用页面编译后的列绑定取值，避免复制一套测试专用绑定掩盖页面漏接。</summary>
    private static async Task Verify(UserControl view, object row, IReadOnlyDictionary<string, object> expected)
    {
        var window = new Window { Content = view, Width = 1300, Height = 800, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grids = Descendants(view).OfType<DataGrid>().ToArray();
            Assert.IsNotEmpty(grids);
            foreach (var pair in expected)
            {
                var column = grids.SelectMany(grid => grid.Columns).OfType<DataGridTemplateColumn>()
                    .Single(column => Equals(column.Header, pair.Key));
                Assert.IsNotNull(column.ClipboardContentBinding, pair.Key);
                Assert.AreEqual(pair.Value, column.OnCopyingCellClipboardContent(row), pair.Key);
            }
            foreach (var grid in grids)
                Assert.IsFalse(grid.CanUserDeleteRows, "原生 Delete 不得回落为删除业务行。");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>遍历实际加载的视觉树，不依赖控件模板的固定层级。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
