using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Components;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>采购详情专用 Lookup、产品回填、客供件目标与创建行顺序的回归测试。</summary>
[TestClass]
public sealed class PurchaseOrderLookupAndReorderTests
{
    [TestMethod]
    public void LookupSelectedItemsMustUseNarrowProcurementDtos()
    {
        Assert.AreEqual(typeof(ProcurementCustomerCandidateDto), PurchaseCustomerLookupTextBox.SelectedCustomerProperty.PropertyType);
        Assert.AreEqual(typeof(ProcurementSupplierCandidateDto), PurchaseSupplierLookupTextBox.SelectedSupplierProperty.PropertyType);
        Assert.AreEqual(typeof(ProcurementProductCandidateDto), PurchaseProductLookupTextBox.SelectedProductProperty.PropertyType);
    }

    [TestMethod]
    public void ProductSelectionMustFillLineAndRequireDeliverySupplierForCustomerSuppliedMaterial()
    {
        var lines = new PurchaseOrderLineListModel();
        var line = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), Quantity = 1m });
        line.ApplyProduct(new ProcurementProductCandidateDto
        {
            Uid = Guid.NewGuid(), Sku = "SKU-1", MainImage = "main.png", CustomerSku = "C-1", LinkSku = "AQ-1",
            SupplierSku = "S-1", DescriptionZh = "描述", FinishZh = "亮铬", FeaturesZh = "配置",
            MaterialType = MaterialTypesEnum.客供件, SupplyType = SupplyTypeEnums.采购, Unit = "PCS",
            UnitPrice = 12.5m, TaxPrice = 14.1m
        });

        Assert.AreEqual("C-1", line.CustomerSku);
        Assert.AreEqual("S-1", line.SupplierSku);
        Assert.AreEqual("AQ-1", line.LinkSku);
        Assert.AreEqual("main.png", line.MainImage);
        Assert.AreEqual(12.5m, line.UnitPrice);
        Assert.AreEqual(14.1m, line.TaxPrice);
        Assert.IsTrue(line.RequiresDeliverySupplier);
        StringAssert.Contains(line.ValidateRow(), "送达供应商");

        line.DeliverySupplier = new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = "SUP", SupplierName = "供应商" };
        Assert.AreEqual("供应商", line.DeliverySupplierName);
        Assert.IsNull(line.ValidateRow(), "客供件明确送达供应商即可通过行校验，不要求采购收货仓。");
    }

    [TestMethod]
    public void NonCustomerSuppliedProductMustClearStaleDeliverySupplier()
    {
        var lines = new PurchaseOrderLineListModel();
        var line = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), Quantity = 1m });
        line.DeliverySupplier = new ProcurementSupplierCandidateDto { Uid = Guid.NewGuid(), SupplierCode = "OLD", SupplierName = "旧目标" };
        line.ApplyProduct(new ProcurementProductCandidateDto
        { Uid = Guid.NewGuid(), Sku = "SKU-2", MaterialType = MaterialTypesEnum.成品, SupplyType = SupplyTypeEnums.采购, Unit = "PCS" });

        Assert.IsFalse(line.RequiresDeliverySupplier);
        Assert.IsNull(line.DeliverySupplierId);
        Assert.AreEqual(string.Empty, line.DeliverySupplierName);
    }

    /// <summary>已保存采购行拒绝通过 Lookup 改变产品身份。</summary>
    [TestMethod]
    public void PersistedLineMustIgnoreProductLookupCommit()
    {
        var originalProductId = Guid.NewGuid();
        var lines = new PurchaseOrderLineListModel();
        lines.LoadFromDtos(new[]
        {
            new PurchaseOrderLineDetailDto
            {
                Uid = Guid.NewGuid(), ProductId = originalProductId, Sku = "OLD", Quantity = 1m,
                UnitSnapshot = "PCS"
            }
        });

        lines.Items[0].ApplyProduct(new ProcurementProductCandidateDto
        { Uid = Guid.NewGuid(), Sku = "NEW", SupplyType = SupplyTypeEnums.采购 });

        Assert.AreEqual(originalProductId, lines.Items[0].ProductId);
        Assert.AreEqual("OLD", lines.Items[0].Sku);
    }

    /// <summary>采购 SKU 编辑器与销售行一致，先暂存候选，再把当前行传给显式提交命令。</summary>
    [TestMethod]
    public void ProductLookupMustUsePageCandidateAndExplicitCommit()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.Client.Wpf/Features/Procurement/PurchaseOrders/Views/PurchaseOrderDetailView.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.Client.Wpf/Features/Procurement/PurchaseOrders/ViewModels/PurchaseOrderDetailViewModel.cs"));

        StringAssert.Contains(xaml, "DataContext.SelectedProductCandidate");
        StringAssert.Contains(xaml, "Mode=OneWayToSource");
        StringAssert.Contains(xaml, "SelectionCommittedCommandParameter=\"{Binding}\"");
        StringAssert.Contains(viewModel, "line.ApplyProduct(SelectedProductCandidate)");
        StringAssert.Contains(viewModel, "SelectedProductCandidate = null");
        StringAssert.Contains(viewModel, "ProductCandidates.Clear()");
    }

    [TestMethod]
    public void MoveMustKeepSelectedLineAndSupportContinuousRenumbering()
    {
        var lines = new PurchaseOrderLineListModel();
        var first = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = 1, Quantity = 1m });
        var second = lines.AddNew(new PurchaseOrderLineDetailDto { Uid = Guid.NewGuid(), LineNumber = 2, Quantity = 1m });
        lines.SelectedItem = second;

        Assert.IsTrue(lines.Move(second, 0));
        for (var index = 0; index < lines.Items.Count; index++) lines.Items[index].LineNumber = index + 1;

        Assert.AreSame(second, lines.SelectedItem);
        Assert.AreEqual(1, second.LineNumber);
        Assert.AreEqual(2, first.LineNumber);
    }

    /// <summary>采购行表必须满足 EnterpriseGrid 的安全重排条件，并为全部业务列声明稳定身份。</summary>
    [TestMethod]
    public void PurchaseLineGridMustUseEnterpriseGridConfigurationAndStableColumnKeys()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root.FullName,
            "SwiftExport.Client.Wpf/Features/Procurement/PurchaseOrders/Views/PurchaseOrderDetailView.xaml"));

        StringAssert.Contains(xaml, "CanUserSortColumns=\"False\"");
        StringAssert.Contains(xaml, "gridOperations:GridOperations.IsEnabled=\"True\"");
        StringAssert.Contains(xaml, "gridOperations:DataGridLineReorder.IsEnabled=\"{Binding IsCreateMode}\"");
        StringAssert.Contains(xaml, "gridSelection:GridSelection.IsCellClickSelectionEnabled=\"True\"");
        StringAssert.Contains(xaml, "gridValidation:GridValidation.IsEnabled=\"True\"");
        StringAssert.Contains(xaml, "<DataTrigger Binding=\"{Binding IsCreateMode}\" Value=\"True\">");

        var expectedKeys = new[]
        {
            "line-number", "image", "sku", "customer-sku", "link-sku", "supplier-sku",
            "description-zh", "finish-zh", "features-zh", "material-type", "supply-type", "unit",
            "quantity", "unit-price", "unit-amount", "planned-delivery-date",
            "delivery-supplier", "source-summary", "note"
        };
        foreach (var key in expectedKeys)
        {
            StringAssert.Contains(xaml, $"gridColumns:GridColumn.Key=\"{key}\"");
        }
        Assert.IsFalse(xaml.Contains("gridColumns:GridColumn.Key=\"warehouse-target\"", StringComparison.Ordinal));
        Assert.IsFalse(xaml.Contains("WarehouseCandidates"));
    }
}
