using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>在真实隔离数据库验证采购头快照筛选、关键词以及日期端点，不访问业务库。</summary>
[TestClass, DoNotParallelize]
public sealed class PurchaseOrderManagerFilterSqlTests
{
    /// <summary>客户代码精确匹配历史快照；关键词包含头行来源，结束日期包含当日末尾并排除次日。</summary>
    [TestMethod]
    public async Task CustomerSupplierKeywordAndInclusiveDatesFilterCorrectOrders()
    {
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var first = await db.CreateAsync(); var second = await db.CreateAsync();
        Assert.IsTrue(first.Success, first.Message); Assert.IsTrue(second.Success, second.Message);
        var uid = first.Data!.PurchaseOrderId; var other = second.Data!.PurchaseOrderId;
        await db.ExecuteAsync("""
            UPDATE dbo.PurchaseOrderHead SET CustomerCode=N'C-HISTORY',OrderDate='2026-09-15T23:59:59.9999999',DemandSourceNo=N'SOURCE-HEAD-UNIQUE' WHERE Uid=@uid;
            UPDATE dbo.PurchaseOrderHead SET CustomerCode=N'C-HISTORY-X',OrderDate='2026-09-16T00:00:00' WHERE Uid=@other;
            UPDATE dbo.PurchaseOrderLine SET SourceNo=N'SOURCE-LINE-UNIQUE',Sku=N'SKU-UNIQUE' WHERE PurchaseOrderId=@uid;
            """, new { uid, other });
        async Task AssertMatches(PurchaseOrderManagerSearchRequest request, int count)
        {
            var result = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>().SearchAsync(request));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(count, result.Data!.TotalCount);
            if (count == 1) Assert.AreEqual(uid, result.Data.Items.Single().PurchaseOrderId);
        }
        await AssertMatches(new() { CustomerCode = "C-HISTORY" }, 1);
        await AssertMatches(new() { CustomerCode = "C-HIST" }, 0);
        await AssertMatches(new() { CustomerCode = "C-HISTORY", SupplierId = db.SupplierId }, 1);
        await AssertMatches(new() { SupplierId = db.ReceiptSupplierId }, 0);
        await AssertMatches(new() { OrderDateFrom = new(2026, 9, 15), OrderDateTo = new(2026, 9, 15) }, 1);
        foreach (var keyword in new[] { first.Data.OrderNo, "SOURCE-HEAD-UNIQUE", "SOURCE-LINE-UNIQUE", "SKU-UNIQUE" })
            await AssertMatches(new() { Keyword = keyword }, 1);
        var invalid = await db.InScopeAsync(p => p.GetRequiredService<IPurchaseOrderManagerQueryService>().SearchAsync(new()
            { OrderDateFrom = new(2026, 9, 16), OrderDateTo = new(2026, 9, 15) }));
        Assert.IsFalse(invalid.Success);
    }
}
