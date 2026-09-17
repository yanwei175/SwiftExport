using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using static SwiftExport.Application.Services.Shipping.PackingListExecutionQuantities;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证客供件批次计算与物理行执行映射的数量守恒。</summary>
[TestClass]
public sealed class PackingListExecutionQuantitiesTests
{
    /// <summary>中点采用远离零舍入且无历史累计输入。</summary>
    [TestMethod]
    public void ComponentRoundsAwayFromZero()
    {
        Assert.AreEqual(0.0001m, ComponentConsumption(1m, 20000m, 1m));
        Assert.AreEqual(0.3333m, ComponentConsumption(1m, 3m, 1m));
        Assert.ThrowsExactly<ArgumentException>(() => ComponentConsumption(1, 0, 1));
    }

    /// <summary>拆箱后比例分配合计等于单次已舍入的耗用量。</summary>
    [TestMethod]
    public void LastLineAbsorbsRoundingRemainder()
    {
        PhysicalLine[] lines = [new(Guid.NewGuid(), 1, 1), new(Guid.NewGuid(), 2, 1), new(Guid.NewGuid(), 3, 1)];
        var result = SplitConsumption(1, lines);
        Assert.AreEqual(1m, result.Sum(x => x.Qty));
        Assert.AreEqual(0.3334m, result[2].Qty);
    }

    /// <summary>极小耗用拆成多行时末行不能因为前行舍入变成负数。</summary>
    [TestMethod]
    public void SmallQuantityNeverCreatesNegativeTail()
    {
        PhysicalLine[] lines = Enumerable.Range(1, 6).Select(i => new PhysicalLine(Guid.NewGuid(), i, 1)).ToArray();
        var result = SplitConsumption(0.0003m, lines);
        Assert.AreEqual(0.0003m, result.Sum(x => x.Qty));
        Assert.IsTrue(result.All(x => x.Qty >= 0));
    }

    /// <summary>分仓执行片段按游标连续归属物理行，不重复分配第一行。</summary>
    [TestMethod]
    public void ShipmentSegmentsPreservePhysicalQuantities()
    {
        PhysicalLine[] lines = [new(Guid.NewGuid(), 1, 8), new(Guid.NewGuid(), 2, 2)];
        var first = AssignShipment(6, 0, lines);
        var second = AssignShipment(4, 6, lines);
        var totals = first.Concat(second).GroupBy(x => x.LineId).ToDictionary(x => x.Key, x => x.Sum(v => v.Qty));
        Assert.AreEqual(8m, totals[lines[0].Id]);
        Assert.AreEqual(2m, totals[lines[1].Id]);
        Assert.ThrowsExactly<ArgumentException>(() => AssignShipment(5, 6, lines));
    }
}
