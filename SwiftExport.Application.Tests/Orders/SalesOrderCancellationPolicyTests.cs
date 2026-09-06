using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Orders;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Tests.Orders;

[TestClass]
public sealed class SalesOrderCancellationPolicyTests
{
    [TestMethod]
    public void Build_ShouldHardDeleteNeverDecomposedDemand()
    {
        var demand = CreateDemand(0);

        var plan = SourceDemandCancellationPolicy.Build(
            [demand],
            new Dictionary<Guid, int> { [demand.Uid] = 0 });

        CollectionAssert.AreEqual(new[] { demand.Uid }, plan.HardDeleteGrossDemandIds.ToArray());
        Assert.IsEmpty(plan.VoidGrossDemandIds);
    }

    [TestMethod]
    public void Build_ShouldVoidDemandWithRolledBackHistory()
    {
        var demand = CreateDemand(2);

        var plan = SourceDemandCancellationPolicy.Build(
            [demand],
            new Dictionary<Guid, int> { [demand.Uid] = 3 });

        Assert.IsEmpty(plan.HardDeleteGrossDemandIds);
        CollectionAssert.AreEqual(new[] { demand.Uid }, plan.VoidGrossDemandIds.ToArray());
    }

    [TestMethod]
    public void Build_ShouldSupportMixedOrder()
    {
        var neverDecomposed = CreateDemand(0);
        var rolledBack = CreateDemand(1);

        var plan = SourceDemandCancellationPolicy.Build(
            [neverDecomposed, rolledBack],
            new Dictionary<Guid, int>
            {
                [neverDecomposed.Uid] = 0,
                [rolledBack.Uid] = 2
            });

        CollectionAssert.AreEqual(new[] { neverDecomposed.Uid }, plan.HardDeleteGrossDemandIds.ToArray());
        CollectionAssert.AreEqual(new[] { rolledBack.Uid }, plan.VoidGrossDemandIds.ToArray());
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    public void Build_ShouldRejectRevisionAndHistoryMismatch(int revision, int historyCount)
    {
        var demand = CreateDemand(revision);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SourceDemandCancellationPolicy.Build(
                [demand],
                new Dictionary<Guid, int> { [demand.Uid] = historyCount }));
    }

    [TestMethod]
    public void Build_ShouldRejectNonPendingDemand()
    {
        var demand = CreateDemand(1);
        demand.DecompositionStatus = GrossDemandDecompositionStatusEnum.Decomposed;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SourceDemandCancellationPolicy.Build(
                [demand],
                new Dictionary<Guid, int> { [demand.Uid] = 1 }));
    }

    private static GrossDemand CreateDemand(int revision) =>
        new()
        {
            Uid = Guid.NewGuid(),
            LifecycleStatus = GrossDemandLifecycleStatusEnum.Active,
            DecompositionStatus = GrossDemandDecompositionStatusEnum.Pending,
            DecompositionRevision = revision
        };
}
