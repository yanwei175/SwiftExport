using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class NetDemandPlanReplacementPlannerTests
{
    private readonly NetDemandPlanReplacementPlanner _planner = new();

    [TestMethod]
    public void ChangingRootSupplier_ReplacesChildDeliveryTargetButKeepsChildPurchaseSupplier()
    {
        var data = CreateFamily();
        var newSupplier = CreateSupplier("S3");

        var plan = _planner.Build(data.RootDemand, data.RootAllocation, data.Demands, data.Allocations,
            DemandExecutionTypeEnum.Purchase, newSupplier, data.RootAllocation.PlannedDeliveryDate,
            SupplierAssignmentSourceEnum.Reassigned, "调整 A 供应商");

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(2, plan.Replacements);
        var newRoot = plan.Replacements.Single(x => x.NetDemandId == data.RootDemand.Uid);
        var newChild = plan.Replacements.Single(x => x.NetDemandId == data.ChildDemand.Uid);
        Assert.AreEqual(newSupplier.Uid, newRoot.PurchaseSupplierId);
        Assert.AreEqual(data.ChildAllocation.PurchaseSupplierId, newChild.PurchaseSupplierId);
        Assert.AreEqual(newRoot.Uid, newChild.DeliveryTargetAllocationId);
        Assert.AreEqual(newSupplier.Uid, newChild.DeliveryTargetSupplierId);
        CollectionAssert.AreEqual(new[] { data.ChildAllocation.Uid }, plan.AffectedCustomerSuppliedAllocationIds.ToArray());
    }

    [TestMethod]
    public void ChangingChildPurchaseSupplier_DoesNotReplaceRoot()
    {
        var data = CreateFamily();
        var newSupplier = CreateSupplier("S3");

        var plan = _planner.Build(data.ChildDemand, data.ChildAllocation, data.Demands, data.Allocations,
            DemandExecutionTypeEnum.Purchase, newSupplier, data.ChildAllocation.PlannedDeliveryDate,
            SupplierAssignmentSourceEnum.Reassigned, "调整 B 采购供应商");

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(1, plan.Replacements);
        var replacement = plan.Replacements[0];
        Assert.AreEqual(data.ChildDemand.Uid, replacement.NetDemandId);
        Assert.AreEqual(newSupplier.Uid, replacement.PurchaseSupplierId);
        Assert.AreEqual(data.RootAllocation.Uid, replacement.DeliveryTargetAllocationId);
        Assert.IsEmpty(plan.AffectedCustomerSuppliedAllocationIds);
    }

    [TestMethod]
    public void ChangingOnlyRootDate_StillMovesChildToNewActiveRootAllocation()
    {
        var data = CreateFamily();
        var existingSupplier = CreateSupplierFromAllocation(data.RootAllocation);

        var plan = _planner.Build(data.RootDemand, data.RootAllocation, data.Demands, data.Allocations,
            DemandExecutionTypeEnum.Purchase, existingSupplier, new DateTime(2026, 8, 8),
            data.RootAllocation.AssignmentSource, "调整计划交期");

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(2, plan.Replacements);
        var newRoot = plan.Replacements.Single(x => x.NetDemandId == data.RootDemand.Uid);
        var newChild = plan.Replacements.Single(x => x.NetDemandId == data.ChildDemand.Uid);
        Assert.AreEqual(new DateTime(2026, 8, 8), newRoot.PlannedDeliveryDate);
        Assert.AreEqual(newRoot.Uid, newChild.DeliveryTargetAllocationId);
        Assert.AreEqual(data.ChildAllocation.PurchaseSupplierId, newChild.PurchaseSupplierId);
    }

    [TestMethod]
    public void ChangingRootSupplier_WithMissingChildAllocation_IsRejected()
    {
        var data = CreateFamily();
        var plan = _planner.Build(data.RootDemand, data.RootAllocation, data.Demands,
            [data.RootAllocation], DemandExecutionTypeEnum.Purchase, CreateSupplier("S3"),
            data.RootAllocation.PlannedDeliveryDate, SupplierAssignmentSourceEnum.Reassigned, "调整");

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("CORRUPTED_STATE", plan.ErrorCode);
    }

    [TestMethod]
    public void CustomerSuppliedChild_CannotSwitchToProduction()
    {
        var data = CreateFamily();
        var plan = _planner.Build(data.ChildDemand, data.ChildAllocation, data.Demands, data.Allocations,
            DemandExecutionTypeEnum.Production, null, data.ChildAllocation.PlannedDeliveryDate,
            SupplierAssignmentSourceEnum.Unassigned, "错误切换");

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("CUSTOMER_SUPPLIED_PURCHASE_REQUIRED", plan.ErrorCode);
    }

    [TestMethod]
    public void SameSupplierId_WithChangedSnapshot_RefreshesRootAndChildSnapshots()
    {
        var data = CreateFamily();
        var refreshed = CreateSupplierFromAllocation(data.RootAllocation);
        refreshed.SupplierName = "供应商新名称";

        var plan = _planner.Build(data.RootDemand, data.RootAllocation, data.Demands, data.Allocations,
            DemandExecutionTypeEnum.Purchase, refreshed, data.RootAllocation.PlannedDeliveryDate,
            SupplierAssignmentSourceEnum.AppliedProductDefault, "刷新默认供应商快照");

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        var newRoot = plan.Replacements.Single(x => x.NetDemandId == data.RootDemand.Uid);
        var newChild = plan.Replacements.Single(x => x.NetDemandId == data.ChildDemand.Uid);
        Assert.AreEqual(data.RootAllocation.PurchaseSupplierId, newRoot.PurchaseSupplierId);
        Assert.AreEqual("供应商新名称", newRoot.SupplierNameSnapshot);
        Assert.AreEqual("供应商新名称", newChild.DeliverySupplierNameSnapshot);
    }

    private static FamilyData CreateFamily()
    {
        var rootSupplier = CreateSupplier("S1");
        var childSupplier = CreateSupplier("S2");
        var rootDemand = CreateDemand("A", 1);
        var childDemand = CreateDemand("B", 2);
        childDemand.RootNetDemandId = rootDemand.Uid;
        childDemand.ParentNetDemandId = rootDemand.Uid;
        childDemand.DeliveryTargetNetDemandId = rootDemand.Uid;
        childDemand.DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier;
        var rootAllocation = CreateAllocation(rootDemand, rootSupplier);
        var childAllocation = CreateAllocation(childDemand, childSupplier);
        childAllocation.DeliveryTargetAllocationId = rootAllocation.Uid;
        childAllocation.DeliveryTargetSupplierId = rootSupplier.Uid;
        childAllocation.DeliverySupplierCodeSnapshot = rootSupplier.SupplierCode;
        childAllocation.DeliverySupplierNameSnapshot = rootSupplier.SupplierName;
        return new FamilyData(rootDemand, childDemand, rootAllocation, childAllocation);
    }

    private static NetDemand CreateDemand(string sku, int line)
    {
        var uid = Guid.NewGuid();
        return new NetDemand
        {
            Uid = uid,
            GrossDemandId = Guid.NewGuid(),
            DecompositionRevision = 1,
            LineNumber = line,
            RootNetDemandId = uid,
            DeliveryMode = DemandDeliveryModeEnum.Standard,
            SupplyTypeSnapshot = "采购",
            SkuSnapshot = sku,
            GrossQty = 10,
            NetQty = 10
        };
    }

    private static NetDemandAllocation CreateAllocation(NetDemand demand, Supplier supplier)
        => new()
        {
            Uid = Guid.NewGuid(),
            NetDemandId = demand.Uid,
            PlanRevision = 1,
            Sequence = 1,
            ExecutionType = DemandExecutionTypeEnum.Purchase,
            PlannedQty = demand.NetQty,
            PlannedDeliveryDate = new DateTime(2026, 8, 10),
            PurchaseSupplierId = supplier.Uid,
            SupplierCodeSnapshot = supplier.SupplierCode,
            SupplierNameSnapshot = supplier.SupplierName,
            AssignmentSource = SupplierAssignmentSourceEnum.ProductDefault
        };

    private static Supplier CreateSupplier(string code)
        => new() { Uid = Guid.NewGuid(), SupplierCode = code, SupplierName = code, DocRelateNo = code };

    private static Supplier CreateSupplierFromAllocation(NetDemandAllocation allocation)
        => new()
        {
            Uid = allocation.PurchaseSupplierId!.Value,
            SupplierCode = allocation.SupplierCodeSnapshot!,
            SupplierName = allocation.SupplierNameSnapshot!,
            DocRelateNo = allocation.SupplierCodeSnapshot!
        };

    private sealed record FamilyData(
        NetDemand RootDemand,
        NetDemand ChildDemand,
        NetDemandAllocation RootAllocation,
        NetDemandAllocation ChildAllocation)
    {
        public IReadOnlyList<NetDemand> Demands => [RootDemand, ChildDemand];
        public IReadOnlyList<NetDemandAllocation> Allocations => [RootAllocation, ChildAllocation];
    }
}
