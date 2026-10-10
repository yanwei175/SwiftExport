using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.SalesOrders.Draft;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>执行草稿头部赋值和详情映射，验证发票更新日期独立存取且不影响日期或价格计算。</summary>
[TestClass]
public sealed class 销售发票更新日期快照测试
{
    /// <summary>快照允许为空、早于或晚于订单日期；保存、详情返回及 JSON 传输都保留原值。</summary>
    [TestMethod]
    public void SnapshotRoundTripsWithoutChangingBusinessDatesOrAmounts()
    {
        var orderDate = new DateTime(2026, 8, 19);
        foreach (var snapshot in new DateTime?[] { null, orderDate.AddDays(-10), orderDate.AddDays(10) })
        {
            var input = new SalesOrderEditHeaderDto { OrderDate = orderDate, InvoiceUpdateDate = snapshot,
                DeliveryDate = orderDate.AddDays(45), Etd = orderDate.AddDays(46), Currency = "CNY" };
            var dto = JsonSerializer.Deserialize<SalesOrderEditHeaderDto>(JsonSerializer.Serialize(input))!;
            var header = new SalesOrderHeader { OrderNo = "SO-1", InvoiceUpdateDate = orderDate };
            var line = new SalesOrderLine { SalesOrderId = Guid.NewGuid(), Sku = "SKU", Qty = 2, CnyAmount = 20, UsdAmount = 3 };
            var apply = typeof(SalesOrderDraftService).GetMethod("ApplyHeaderChanges", BindingFlags.NonPublic | BindingFlags.Static)!;
            apply.Invoke(null, [header, dto, new Customer { CustomerCode = "C", DocRelateNo = "C" }, new[] { line }]);
            Assert.AreEqual(snapshot, header.InvoiceUpdateDate);
            Assert.AreEqual(orderDate, header.OrderDate); Assert.AreEqual(input.DeliveryDate, header.DeliveryDate);
            Assert.AreEqual(input.Etd, header.ETD); Assert.AreEqual(20m, header.TotalAmountLocalCurrency);
            Assert.AreEqual(20m, header.TotalAmountOriginalCurrency);
            var map = typeof(PostgreSqlSalesOrderQueryRepository).GetMethod("MapHeader", BindingFlags.NonPublic | BindingFlags.Static)!;
            var detail = (SalesOrderEditHeaderDto)map.Invoke(null, [header])!;
            Assert.AreEqual(snapshot, detail.InvoiceUpdateDate); Assert.AreEqual(orderDate, detail.OrderDate);
        }
        Assert.IsNull(JsonSerializer.Deserialize<SalesOrderEditHeaderDto>("{}")!.InvoiceUpdateDate);
    }

    /// <summary>实际详情头查询显式读取新列，避免实体保存成功但读回丢失快照。</summary>
    [TestMethod]
    public void DetailProjectionIncludesSnapshotColumn()
    {
        var projection = (string)typeof(PostgreSqlSalesOrderQueryRepository)
            .GetField("HeaderProjection", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        StringAssert.Contains(projection, "\"invoice_update_date\" AS \"InvoiceUpdateDate\"");
    }
}
