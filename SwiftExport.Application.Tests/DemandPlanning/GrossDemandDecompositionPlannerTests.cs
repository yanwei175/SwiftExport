using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class GrossDemandDecompositionPlannerTests
{
    private readonly GrossDemandDecompositionPlanner _planner = new();

    [TestMethod]
    public void NonStandard_CreatesSinglePurchasedRoot()
    {
        var gross = CreateGross(isNonStandard: true, productId: null, qty: 12);

        var plan = _planner.BuildPlan(gross, null, [], new Dictionary<Guid, Product>(), 1);

        Assert.IsTrue(plan.IsValid);
        Assert.HasCount(1, plan.NetDemands);
        var row = plan.NetDemands[0];
        Assert.AreEqual(SupplyTypeEnums.采购, row.SupplyTypeSnapshot);
        Assert.AreEqual(row.Uid, row.RootNetDemandId);
        Assert.IsNull(row.ProductId);
        Assert.AreEqual(12m, row.NetQty);
    }

    [TestMethod]
    public void PurchasedRoot_OnlyExtractsDirectCustomerSuppliedChildren()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, bomVersion: 1, bomNo: "BOM-A");
        var supplied = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var ordinary = CreateProduct("C", SupplyTypeEnums.采购, MaterialTypesEnum.零件);
        var gross = CreateGross(false, root.Uid, 10);
        var lines = new[]
        {
            CreateBom(root, supplied, 1, 2, 1),
            CreateBom(root, ordinary, 2, 5, 1)
        };

        var plan = _planner.BuildPlan(gross, root, lines,
            new Dictionary<Guid, Product> { [supplied.Uid] = supplied, [ordinary.Uid] = ordinary }, 1);

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(2, plan.NetDemands);
        var rootRow = plan.NetDemands.Single(x => x.LineNumber == 1);
        var suppliedRow = plan.NetDemands.Single(x => x.LineNumber == 2);
        Assert.AreEqual("A", rootRow.SkuSnapshot);
        Assert.AreEqual("B", suppliedRow.SkuSnapshot);
        Assert.AreEqual(20m, suppliedRow.NetQty);
        Assert.AreEqual(rootRow.Uid, suppliedRow.ParentNetDemandId);
        Assert.AreEqual(rootRow.Uid, suppliedRow.RootNetDemandId);
        Assert.AreEqual(rootRow.Uid, suppliedRow.DeliveryTargetNetDemandId);
        Assert.AreEqual(DemandDeliveryModeEnum.ParentExecutionSupplier, suppliedRow.DeliveryMode);
        Assert.IsFalse(plan.NetDemands.Any(x => x.SkuSnapshot == "C"));
    }

    [TestMethod]
    public void PurchasedRootWithoutBom_CreatesOnlyRoot()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品);
        var gross = CreateGross(false, root.Uid, 4);

        var plan = _planner.BuildPlan(gross, root, [], new Dictionary<Guid, Product>(), 1);

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(1, plan.NetDemands);
        Assert.AreEqual("A", plan.NetDemands[0].SkuSnapshot);
        Assert.AreEqual(4m, plan.NetDemands[0].NetQty);
    }

    [TestMethod]
    public void PurchasedRootWithOrdinaryChildren_CreatesOnlyRoot()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "BOM-A");
        var ordinary = CreateProduct("C", SupplyTypeEnums.采购, MaterialTypesEnum.零件);
        var gross = CreateGross(false, root.Uid, 3);

        var plan = _planner.BuildPlan(gross, root, [CreateBom(root, ordinary, 1, 2, 1)],
            new Dictionary<Guid, Product> { [ordinary.Uid] = ordinary }, 1);

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(1, plan.NetDemands);
        Assert.AreEqual("A", plan.NetDemands[0].SkuSnapshot);
    }

    [TestMethod]
    public void DuplicateCustomerSuppliedBomLines_RemainSeparateAndStable()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "BOM-A");
        var supplied = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var gross = CreateGross(false, root.Uid, 10);
        var first = CreateBom(root, supplied, 2, 1, 1);
        var second = CreateBom(root, supplied, 1, 2, 1);

        var plan = _planner.BuildPlan(gross, root, [first, second],
            new Dictionary<Guid, Product> { [supplied.Uid] = supplied }, 1);

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(3, plan.NetDemands);
        var suppliedRows = plan.NetDemands.Skip(1).ToArray();
        CollectionAssert.AreEqual(new[] { 2, 3 }, suppliedRows.Select(x => x.LineNumber).ToArray());
        CollectionAssert.AreEqual(new[] { second.Uid, first.Uid }, suppliedRows.Select(x => x.BomLineId!.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 20m, 10m }, suppliedRows.Select(x => x.NetQty).ToArray());
    }

    [TestMethod]
    public void OutsourcedRoot_ExtractsDirectCustomerSuppliedChild()
    {
        var root = CreateProduct("A", SupplyTypeEnums.外协, MaterialTypesEnum.成品, 1, "BOM-A");
        var supplied = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var gross = CreateGross(false, root.Uid, 2);

        var plan = _planner.BuildPlan(gross, root, [CreateBom(root, supplied, 1, 3, 1)],
            new Dictionary<Guid, Product> { [supplied.Uid] = supplied }, 1);

        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(2, plan.NetDemands);
        Assert.AreEqual(SupplyTypeEnums.外协, plan.NetDemands[0].SupplyTypeSnapshot);
        Assert.AreEqual(6m, plan.NetDemands[1].NetQty);
    }

    [TestMethod]
    public void DeclaredBomWithoutRows_IsRejected()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "BOM-A");
        var gross = CreateGross(false, root.Uid, 1);

        var plan = _planner.BuildPlan(gross, root, [], new Dictionary<Guid, Product>(), 1);

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("BOM_NOT_FOUND", plan.ErrorCode);
    }

    [TestMethod]
    public void CustomerSuppliedWithInvalidSupplyType_IsRejected()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "BOM-A");
        var supplied = CreateProduct("B", SupplyTypeEnums.生产, MaterialTypesEnum.客供件);
        var gross = CreateGross(false, root.Uid, 1);

        var plan = _planner.BuildPlan(gross, root, [CreateBom(root, supplied, 1, 1, 1)],
            new Dictionary<Guid, Product> { [supplied.Uid] = supplied }, 1);

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("INVALID_BOM_LINE", plan.ErrorCode);
    }

    [TestMethod]
    public void ProductionRootWithCustomerSuppliedChild_IsExplicitlyRejected()
    {
        var root = CreateProduct("A", SupplyTypeEnums.生产, MaterialTypesEnum.成品, 1, "BOM-A");
        var supplied = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var gross = CreateGross(false, root.Uid, 1);

        var plan = _planner.BuildPlan(gross, root, [CreateBom(root, supplied, 1, 1, 1)],
            new Dictionary<Guid, Product> { [supplied.Uid] = supplied }, 1);

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("PRODUCTION_CUSTOMER_SUPPLIED_NOT_SUPPORTED", plan.ErrorCode);
    }

    [TestMethod]
    public void CustomerSuppliedRoot_IsRejected()
    {
        var root = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var gross = CreateGross(false, root.Uid, 1);

        var plan = _planner.BuildPlan(gross, root, [], new Dictionary<Guid, Product>(), 1);

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("CUSTOMER_SUPPLIED_ROOT_NOT_ALLOWED", plan.ErrorCode);
    }

    [TestMethod]
    public void InvalidDenominator_IsRejectedBeforeCalculation()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "BOM-A");
        var supplied = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var gross = CreateGross(false, root.Uid, 1);

        var plan = _planner.BuildPlan(gross, root, [CreateBom(root, supplied, 1, 1, 0)],
            new Dictionary<Guid, Product> { [supplied.Uid] = supplied }, 1);

        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("INVALID_BOM_LINE", plan.ErrorCode);
    }

    /// <summary>根行数量沿用来源单位，客供子件数量采用拆解时子产品单位，后续产品变更不改写计划快照。</summary>
    [TestMethod]
    public void UnitsFollowSourceRootAndComponentProduct()
    {
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "BOM-A");
        root.Unit = "CURRENT-ROOT";
        var child = CreateProduct("B", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        child.Unit = " KG ";
        var gross = CreateGross(false, root.Uid, 10); gross.UnitSnapshot = " SET ";
        var plan = _planner.BuildPlan(gross, root, [CreateBom(root, child, 1, 2, 1)],
            new Dictionary<Guid, Product> { [child.Uid] = child }, 1);
        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.AreEqual("SET", plan.NetDemands[0].UnitSnapshot);
        Assert.AreEqual("KG", plan.NetDemands[1].UnitSnapshot);
        child.Unit = "PCS"; root.Unit = "PCS";
        Assert.AreEqual("SET", plan.NetDemands[0].UnitSnapshot);
        Assert.AreEqual("KG", plan.NetDemands[1].UnitSnapshot);
    }

    /// <summary>非标根保留明确单位，历史来源缺单位时保持未知，不借当前产品补值。</summary>
    [TestMethod]
    public void NonStandardAndUnknownUnitsRemainSourceFacts()
    {
        var gross = CreateGross(true, null, 3); gross.UnitSnapshot = "BOX";
        Assert.AreEqual("BOX", _planner.BuildPlan(gross, null, [], new Dictionary<Guid, Product>(), 1).NetDemands[0].UnitSnapshot);
        gross.UnitSnapshot = null;
        Assert.IsNull(_planner.BuildPlan(gross, null, [], new Dictionary<Guid, Product>(), 1).NetDemands[0].UnitSnapshot);
        var root = CreateProduct("A", SupplyTypeEnums.采购, MaterialTypesEnum.成品); root.Unit = "PCS";
        gross.IsNonStandard = false; gross.ProductId = root.Uid;
        Assert.IsNull(_planner.BuildPlan(gross, root, [], new Dictionary<Guid, Product>(), 1).NetDemands[0].UnitSnapshot);
    }

    private static GrossDemand CreateGross(bool isNonStandard, Guid? productId, decimal qty)
        => new()
        {
            ProductId = productId,
            IsNonStandard = isNonStandard,
            SkuSnapshot = isNonStandard ? "临时-SKU" : "A",
            DescriptionSnapshot = "描述",
            DemandQty = qty,
            SourceType = DocumentTypeStaticsEnum.ManualDemand,
            SourceUid = Guid.NewGuid(),
            SourceLineId = Guid.NewGuid(),
            SourceNo = "MD-1"
        };

    private static Product CreateProduct(string sku, string supplyType, string materialType, int bomVersion = 0, string? bomNo = null)
        => new()
        {
            Uid = Guid.NewGuid(),
            Sku = sku,
            SupplyType = supplyType,
            MaterialType = materialType,
            BomVersion = bomVersion,
            BomNo = bomNo,
            DescriptionEn = sku + " desc"
        };

    private static Bom CreateBom(Product parent, Product child, int line, int numerator, int denominator)
        => new()
        {
            Uid = Guid.NewGuid(),
            BomNo = parent.BomNo!,
            BomVersion = parent.BomVersion,
            ParentProductId = parent.Uid,
            ParentSkuSnapshot = parent.Sku,
            LineNumber = line,
            ComponentProductId = child.Uid,
            ComponentSkuSnapshot = child.Sku,
            Numerator = numerator,
            Denominator = denominator
        };
}
