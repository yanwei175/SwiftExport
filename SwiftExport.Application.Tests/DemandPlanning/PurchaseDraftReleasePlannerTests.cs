using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.PurchaseOrders;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class PurchaseDraftReleasePlannerTests
{
    [TestMethod]
    public void Build_ShouldReleaseByDateThenSourceLineThenAllocationDescending()
    {
        var planner = new PurchaseDraftReleasePlanner();
        var early = Link("10000000-0000-0000-0000-000000000001", "20000000-0000-0000-0000-000000000001", 1, new DateTime(2026, 8, 10), 4);
        var lateLowLine = Link("10000000-0000-0000-0000-000000000002", "20000000-0000-0000-0000-000000000002", 1, new DateTime(2026, 8, 12), 3);
        var lateHighLine = Link("10000000-0000-0000-0000-000000000003", "20000000-0000-0000-0000-000000000003", 2, new DateTime(2026, 8, 12), 5);

        var result = planner.Build(Guid.NewGuid(), 3, 7, [early, lateLowLine, lateHighLine]);

        Assert.IsFalse(result.HasBlockers);
        Assert.AreEqual(5m, result.ReleaseQty);
        Assert.HasCount(1, result.Releases);
        Assert.AreEqual(lateHighLine.LinkId, result.Releases[0].LinkId);
        Assert.AreEqual(0m, result.Releases[0].RemainingEffectiveQty);
        Assert.AreEqual(64, result.PreviewToken.Length);
    }

    [TestMethod]
    public void Build_ShouldRejectIncreaseAndUseReleasedEffectiveQuantity()
    {
        var planner = new PurchaseDraftReleasePlanner();
        var link = Link("10000000-0000-0000-0000-000000000004", "20000000-0000-0000-0000-000000000004", 1, null, 10);
        link.AllocatedQty = 15;
        link.ReleasedQty = 5;

        var result = planner.Build(Guid.NewGuid(), 1, 11, [link]);

        Assert.IsTrue(result.HasBlockers);
        StringAssert.Contains(string.Join(";", result.Blockers), "增加数量");
    }

    private static PurchaseDraftLifecycleLinkFactDto Link(
        string linkId,
        string allocationId,
        int sourceLine,
        DateTime? date,
        decimal qty) => new()
        {
            LinkId = Guid.Parse(linkId),
            AllocationId = Guid.Parse(allocationId),
            PurchaseOrderLineId = Guid.NewGuid(),
            SourceLineNumber = sourceLine,
            PlannedDeliveryDate = date,
            AllocatedQty = qty,
            EffectiveQty = qty,
        };
}
