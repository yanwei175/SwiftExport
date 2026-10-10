using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.SalesOrders.Draft;
using SwiftExport.Application.Services.SalesOrders.Internal;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>执行销售草稿实际头部规则和查询映射，核对废弃汇率兼容及价格快照；不连接数据库。</summary>
[TestClass]
public sealed class 销售汇率弃用测试
{
    /// <summary>新建不接纳旧客户端汇率，更新保留历史原值；两币种汇总各自来自原价金额。</summary>
    [TestMethod]
    public void DraftIgnoresSubmittedRateAndPreservesHistoricalStorage()
    {
        foreach (var stored in new decimal?[] { null, 0m, -3m, 7.2m })
        foreach (var submitted in new decimal?[] { null, 0m, -9m, 9m })
        foreach (var currency in new[] { "CNY", "USD" })
        {
            var header = new SalesOrderHeader { ExchangeRate = stored, OrderNo = "SO-1" };
            var dto = new SalesOrderEditHeaderDto { OrderDate = DateTime.Today, Currency = currency, ExchangeRate = submitted };
            var line = new SalesOrderLine { SalesOrderId = Guid.NewGuid(), Sku = "CUSTOM", Qty = 2, CnyPrice = 37.7m, UsdPrice = 5.18m, CnyAmount = 75.4m, UsdAmount = 10.36m };
            Apply(header, dto, [line]);
            Assert.AreEqual(stored, header.ExchangeRate); Assert.AreEqual(currency, header.Currency);
            Assert.AreEqual(75.4m, header.TotalAmountLocalCurrency);
            Assert.AreEqual(currency == "CNY" ? 75.4m : 10.36m, header.TotalAmountOriginalCurrency);
            Assert.AreEqual(37.7m, line.CnyPrice); Assert.AreEqual(5.18m, line.UsdPrice);
        }
    }

    /// <summary>取消汇率门禁不取消日期和定金校验，非法活动字段仍拒绝保存。</summary>
    [TestMethod]
    public void DraftStillRequiresDateAndValidDeposit()
    {
        var header = new SalesOrderHeader { OrderNo = "SO-1" };
        var dto = new SalesOrderEditHeaderDto { ExchangeRate = null };
        Assert.ThrowsExactly<SalesOrderValidationException>(() => Apply(header, dto, []));
        dto.OrderDate = DateTime.Today;
        foreach (var deposit in new[] { -0.1m, 1.1m })
        {
            dto.DepositRate = deposit;
            Assert.ThrowsExactly<SalesOrderValidationException>(() => Apply(header, dto, []));
        }
    }

    /// <summary>缺失、空和旧数值可反序列化；详情返回废弃字段原值，避免读后保存改写历史。</summary>
    [TestMethod]
    public void ContractAndDetailPreserveNullableLegacyValues()
    {
        Assert.IsNull(JsonSerializer.Deserialize<SalesOrderEditHeaderDto>("{}")!.ExchangeRate);
        foreach (var rate in new decimal?[] { null, 0m, -3m, 7.2m })
        {
            var json = JsonSerializer.Serialize(new SalesOrderEditHeaderDto { ExchangeRate = rate });
            Assert.AreEqual(rate, JsonSerializer.Deserialize<SalesOrderEditHeaderDto>(json)!.ExchangeRate);
            var header = new SalesOrderHeader { ExchangeRate = rate, OrderNo = "SO-1" };
            var map = typeof(PostgreSqlSalesOrderQueryRepository).GetMethod("MapHeader", BindingFlags.NonPublic | BindingFlags.Static)!;
            var detail = (SalesOrderEditHeaderDto)map.Invoke(null, [header])!;
            Assert.AreEqual(rate, detail.ExchangeRate);
        }
    }

    /// <summary>调用草稿保存实际使用的头部规则，并还原业务拒绝异常，避免另写一套测试算法。</summary>
    private static void Apply(SalesOrderHeader header, SalesOrderEditHeaderDto dto, IReadOnlyList<SalesOrderLine> lines)
    {
        var method = typeof(SalesOrderDraftService).GetMethod("ApplyHeaderChanges", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { method.Invoke(null, [header, dto, new Customer { Uid = Guid.NewGuid(), CustomerCode = "C", DocRelateNo = "C" }, lines]); }
        catch (TargetInvocationException ex) when (ex.InnerException is SalesOrderValidationException validation) { throw validation; }
    }
}
