using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using static SwiftExport.Application.Services.Shipping.PackingListCapacityAllocator;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证全单容量守恒和残量回退，不使用数据库证明事务行为。</summary>
[TestClass]
public sealed class PackingListCapacityAllocatorTests
{
    private static Guid Id(int number) => new(number, 0, 0, new byte[8]);

    /// <summary>将已有库存让给不能补入库的来源，不因先选来源而阻断。</summary>
    [TestMethod]
    public void ReassignsStockToSourceWithoutPurchaseCapacity()
    {
        var result = new PackingListCapacityAllocator().Allocate(
            [new(Id(1), 10, false), new(Id(2), 10, true)],
            [new(Id(3), 10, false), new(Id(4), 10, false)],
            [new(Id(1), Id(3), 0), new(Id(1), Id(4), 1), new(Id(2), Id(3), 2)]);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(10m, result.Allocations.Single(x => x.SupplyId == Id(1)).Quantity);
        Assert.AreEqual(Id(4), result.Allocations.Single(x => x.SupplyId == Id(1)).DemandId);
    }

    /// <summary>共享采购行冲突可通过反向边撤回先前分配。</summary>
    [TestMethod]
    public void ReassignsSharedPurchaseCapacity()
    {
        var result = new PackingListCapacityAllocator().Allocate(
            [new(Id(1), 5, true), new(Id(2), 5, true)],
            [new(Id(3), 5, false), new(Id(4), 5, false)],
            [new(Id(1), Id(3), 0), new(Id(1), Id(4), 1), new(Id(2), Id(3), 2)]);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(Id(4), result.Allocations.Single(x => x.SupplyId == Id(1)).DemandId);
    }

    /// <summary>仓库偏好不能迫使使用本可避免的新增入库。</summary>
    [TestMethod]
    public void MinimizesReceiptBeforeSecondaryPriority()
    {
        var result = new PackingListCapacityAllocator().Allocate(
            [new(Id(1), 1, false), new(Id(2), 1, true)], [new(Id(3), 1, false)],
            [new(Id(1), Id(3), int.MaxValue), new(Id(2), Id(3), 0)]);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(Id(1), result.Allocations.Single().SupplyId);
    }

    /// <summary>成品与同产品客供件共用余额，新增收货只能覆盖成品。</summary>
    [TestMethod]
    public void ComponentsAndFinishedGoodsShareStock()
    {
        var result = new PackingListCapacityAllocator().Allocate(
            [new(Id(1), 5, false), new(Id(2), 5, true)],
            [new(Id(3), 5, false), new(Id(4), 5, true)],
            [new(Id(1), Id(3), 0), new(Id(1), Id(4), 1), new(Id(2), Id(3), 0)]);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(Id(4), result.Allocations.Single(x => x.SupplyId == Id(1)).DemandId);
    }

    /// <summary>不可行时不能泄漏可以被误执行的部分方案。</summary>
    [TestMethod]
    public void InfeasibleResultContainsOnlyShortfalls()
    {
        var result = new PackingListCapacityAllocator().Allocate([new(Id(1), 1.0001m, false)],
            [new(Id(2), 2, false)], [new(Id(1), Id(2), 0)]);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.Allocations.Count);
        Assert.AreEqual(0.9999m, result.Shortfalls[Id(2)]);
    }

    /// <summary>非法的自动补料边立即拒绝。</summary>
    [TestMethod]
    public void RejectsAutomaticComponentReceipt()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PackingListCapacityAllocator().Allocate(
            [new(Id(1), 1, true)], [new(Id(2), 1, true)], [new(Id(1), Id(2), 0)]));
    }

    /// <summary>重复容量身份及超过四位小数不能被悄悄合并或舍入。</summary>
    [TestMethod]
    public void RejectsDuplicateCapacityAndExcessPrecision()
    {
        var allocator = new PackingListCapacityAllocator();
        Assert.ThrowsExactly<ArgumentException>(() => allocator.Allocate(
            [new(Id(1), 1, false), new(Id(1), 1, false)], [], []));
        Assert.ThrowsExactly<ArgumentException>(() => allocator.Allocate(
            [new(Id(1), 0.00001m, false)], [], []));
    }

    /// <summary>输入枚举顺序不影响确定性分配和后续预览指纹。</summary>
    [TestMethod]
    public void InputOrderDoesNotChangeResult()
    {
        Supply[] supplies = [new(Id(1), 2, false), new(Id(2), 2, false)];
        Demand[] demands = [new(Id(3), 2, false), new(Id(4), 2, false)];
        Candidate[] edges = [new(Id(1), Id(3), 0), new(Id(1), Id(4), 0), new(Id(2), Id(3), 0), new(Id(2), Id(4), 0)];
        var allocator = new PackingListCapacityAllocator();
        var expected = allocator.Allocate(supplies, demands, edges);
        var actual = allocator.Allocate(supplies.Reverse().ToArray(), demands.Reverse().ToArray(), edges.Reverse().ToArray());
        CollectionAssert.AreEqual(expected.Allocations.ToArray(), actual.Allocations.ToArray());
    }
}
