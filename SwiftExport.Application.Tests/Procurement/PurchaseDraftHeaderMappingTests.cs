using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证采购创建只读单头的来源、配置和空值业务口径。</summary>
[TestClass]
public sealed class PurchaseDraftHeaderMappingTests
{
    /// <summary>销售来源按三段值直接拼接，并完整采用客户供应商配置而不计算折扣。</summary>
    [TestMethod]
    public void BuildNewHeader_SalesSourceUsesSourceAndConfigurationSnapshots()
    {
        var expectedDate = new DateTime(2026, 9, 20);
        var header = PurchaseDraftPreviewService.BuildNewHeader(
            new Customer { CustomerCode = "C001", DocRelateNo = "C-" },
            new Supplier { SupplierCode = "S001", SupplierName = "供应商", DocRelateNo = "-S" },
            new SalesOrderHeader
            {
                OrderNo = "SO001",
                InvoiceNo = "INV",
                CustomerPoNo = "PO-CUSTOMER"
            },
            new CustomerPoConfigDto
            {
                Currency = "USD",
                DefaultDiscountRate = 0.95m,
                DiscountRemark = "倍率说明",
                InletHoseType = "软管 A",
                Purchaser = "采购员甲",
                Note = "特殊要求"
            },
            expectedDate);

        Assert.AreEqual("C-INV-S", header.BoNo);
        Assert.AreEqual("PO-CUSTOMER", header.CustomerPoNo);
        Assert.AreEqual("USD", header.Currency);
        Assert.AreEqual(0.95m, header.DiscountRate);
        Assert.IsNull(header.DiscountAmount);
        Assert.AreEqual("倍率说明", header.DiscountRemark);
        Assert.AreEqual("软管 A", header.InletHoseType);
        Assert.AreEqual("采购员甲", header.PurchasePerson);
        Assert.AreEqual("特殊要求", header.Remark);
        Assert.AreEqual(expectedDate, header.ExpectedDate);
        Assert.IsNull(header.OrderDate);
        Assert.AreEqual(string.Empty, header.OrderNo);
    }

    /// <summary>独立来源没有销售头时保持业务关联号与客户 PO 为空，且不把缺失配置伪装成 CNY。</summary>
    [TestMethod]
    public void BuildNewHeader_IndependentSourceDoesNotGuessSalesOrCurrencyFacts()
    {
        var header = PurchaseDraftPreviewService.BuildNewHeader(
            new Customer { CustomerCode = "C001", DocRelateNo = "C-" },
            new Supplier { SupplierCode = "S001", SupplierName = "供应商", DocRelateNo = "-S" },
            salesHead: null,
            config: null,
            expectedDate: null);

        Assert.AreEqual(string.Empty, header.BoNo);
        Assert.AreEqual(string.Empty, header.CustomerPoNo);
        Assert.AreEqual(string.Empty, header.Currency);
        Assert.IsNull(header.DiscountRate);
        Assert.IsNull(header.DiscountAmount);
        Assert.IsNull(header.ExpectedDate);
    }
}
