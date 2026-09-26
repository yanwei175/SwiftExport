using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Application.Services.DemandPlanning.Sources.Policies;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Tests.Orders;

[TestClass]
public sealed class SalesOrderCancellationPolicyTests
{
    [TestMethod]
    public void Build_ShouldHardDeleteNeverDecomposedDemand()
    {
        var demand = CreateDemand();

        var plan = SourceDemandCancellationPolicy.Build(
            [demand],
            new Dictionary<Guid, int> { [demand.Uid] = 0 });

        CollectionAssert.AreEqual(new[] { demand.Uid }, plan.HardDeleteGrossDemandIds.ToArray());
        Assert.IsEmpty(plan.VoidGrossDemandIds);
    }

    [TestMethod]
    public void Build_ShouldVoidDemandWithRolledBackHistory()
    {
        var demand = CreateDemand();

        var plan = SourceDemandCancellationPolicy.Build(
            [demand],
            new Dictionary<Guid, int> { [demand.Uid] = 3 });

        Assert.IsEmpty(plan.HardDeleteGrossDemandIds);
        CollectionAssert.AreEqual(new[] { demand.Uid }, plan.VoidGrossDemandIds.ToArray());
    }

    [TestMethod]
    public void Build_ShouldSupportMixedOrder()
    {
        var neverDecomposed = CreateDemand();
        var rolledBack = CreateDemand();

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
    public void Build_ShouldRejectInactiveDemand()
    {
        var demand = CreateDemand();
        demand.LifecycleStatus = GrossDemandLifecycleStatusEnum.Voided;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SourceDemandCancellationPolicy.Build(
                [demand],
                new Dictionary<Guid, int> { [demand.Uid] = 1 }));
    }

    private static GrossDemand CreateDemand() =>
        new()
        {
            Uid = Guid.NewGuid(),
            LifecycleStatus = GrossDemandLifecycleStatusEnum.Active
        };
}
