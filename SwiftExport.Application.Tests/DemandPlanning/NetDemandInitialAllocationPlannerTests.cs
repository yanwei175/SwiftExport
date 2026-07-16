using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class NetDemandInitialAllocationPlannerTests
{
    private readonly NetDemandInitialAllocationPlanner _planner = new();

    [TestMethod]
    public void PurchasedRoot_UsesProductDefaultSupplier()
    {
        var supplier = CreateSupplier("S01");
        var product = CreateProduct("A", SupplyTypeEnums.采购, supplier);
        var demand = CreateDemand("A", product, 12m);

        var result = _planner.Build([demand],
            new Dictionary<Guid, Product> { [product.Uid] = product },
            new Dictionary<Guid, Supplier> { [supplier.Uid] = supplier });

        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        var allocation = result.Allocations.Single();
        Assert.AreEqual(DemandExecutionTypeEnum.Purchase, allocation.ExecutionType);
        Assert.AreEqual(supplier.Uid, allocation.PurchaseSupplierId);
        Assert.AreEqual(SupplierAssignmentSourceEnum.ProductDefault, allocation.AssignmentSource);
        Assert.AreEqual(12m, allocation.PlannedQty);
        Assert.AreEqual(demand.RequiredDate, allocation.PlannedDeliveryDate);
    }

    [TestMethod]
    public void PurchasedRoot_WithoutDefaultSupplier_RemainsUnassigned()
    {
        var product = CreateProduct("A", SupplyTypeEnums.采购);
        var demand = CreateDemand("A", product, 2m);

        var result = _planner.Build([demand],
            new Dictionary<Guid, Product> { [product.Uid] = product },
            new Dictionary<Guid, Supplier>());

        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        Assert.IsNull(result.Allocations[0].PurchaseSupplierId);
        Assert.AreEqual(SupplierAssignmentSourceEnum.Unassigned, result.Allocations[0].AssignmentSource);
    }

    [TestMethod]
    public void CustomerSuppliedChild_UsesOwnSupplierAndDeliversToRootSupplier()
    {
        var rootSupplier = CreateSupplier("ROOT");
        var childSupplier = CreateSupplier("CHILD");
        var rootProduct = CreateProduct("A", SupplyTypeEnums.采购, rootSupplier);
        var childProduct = CreateProduct("B", SupplyTypeEnums.采购, childSupplier);
        var root = CreateDemand("A", rootProduct, 5m);
        var child = CreateDemand("B", childProduct, 10m);
        child.RootNetDemandId = root.Uid;
        child.ParentNetDemandId = root.Uid;
        child.DeliveryTargetNetDemandId = root.Uid;
        child.DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier;

        var result = _planner.Build([root, child],
            new Dictionary<Guid, Product>
            {
                [rootProduct.Uid] = rootProduct,
                [childProduct.Uid] = childProduct
            },
            new Dictionary<Guid, Supplier>
            {
                [rootSupplier.Uid] = rootSupplier,
                [childSupplier.Uid] = childSupplier
            });

        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        var rootAllocation = result.Allocations.Single(x => x.NetDemandId == root.Uid);
        var childAllocation = result.Allocations.Single(x => x.NetDemandId == child.Uid);
        Assert.AreEqual(childSupplier.Uid, childAllocation.PurchaseSupplierId);
        Assert.AreEqual(rootAllocation.Uid, childAllocation.DeliveryTargetAllocationId);
        Assert.AreEqual(rootSupplier.Uid, childAllocation.DeliveryTargetSupplierId);
    }

    [TestMethod]
    public void InvalidDefaultSupplierUid_IsRejected()
    {
        var product = CreateProduct("A", SupplyTypeEnums.采购);
        product.DefaultSupplierId = Guid.NewGuid();
        var demand = CreateDemand("A", product, 1m);

        var result = _planner.Build([demand],
            new Dictionary<Guid, Product> { [product.Uid] = product },
            new Dictionary<Guid, Supplier>());

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("INVALID_DEFAULT_SUPPLIER", result.ErrorCode);
    }

    [TestMethod]
    public void ProductionPlan_DoesNotTreatDefaultSupplierAsPurchaseSupplier()
    {
        var supplier = CreateSupplier("S01");
        var product = CreateProduct("A", SupplyTypeEnums.生产, supplier);
        var demand = CreateDemand("A", product, 1m);

        var result = _planner.Build([demand],
            new Dictionary<Guid, Product> { [product.Uid] = product },
            new Dictionary<Guid, Supplier> { [supplier.Uid] = supplier });

        Assert.IsTrue(result.IsValid, result.ErrorMessage);
        Assert.AreEqual(DemandExecutionTypeEnum.Production, result.Allocations[0].ExecutionType);
        Assert.IsNull(result.Allocations[0].PurchaseSupplierId);
    }

    private static Product CreateProduct(string sku, string supplyType, Supplier? supplier = null)
        => new()
        {
            Uid = Guid.NewGuid(),
            Sku = sku,
            SupplyType = supplyType,
            DefaultSupplierId = supplier?.Uid,
            DefaultSupplierCode = supplier?.SupplierCode
        };

    private static Supplier CreateSupplier(string code)
        => new()
        {
            Uid = Guid.NewGuid(),
            SupplierCode = code,
            SupplierName = code + " supplier",
            DocRelateNo = code
        };

    private static NetDemand CreateDemand(string sku, Product product, decimal quantity)
    {
        var uid = Guid.NewGuid();
        return new NetDemand
        {
            Uid = uid,
            GrossDemandId = Guid.NewGuid(),
            DecompositionRevision = 1,
            LineNumber = sku == "A" ? 1 : 2,
            RootNetDemandId = uid,
            ProductId = product.Uid,
            SupplyTypeSnapshot = product.SupplyType,
            SkuSnapshot = sku,
            GrossQty = quantity,
            NetQty = quantity,
            RequiredDate = new DateTime(2026, 8, 10)
        };
    }
}
