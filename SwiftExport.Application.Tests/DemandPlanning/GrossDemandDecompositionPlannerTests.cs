using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证真实根产品下的直接客供件展开、用量计算及不可回写的来源快照。</summary>
[TestClass]
public sealed class GrossDemandDecompositionPlannerTests
{
    private readonly GrossDemandDecompositionPlanner _planner = new();

    /// <summary>不同正标记引用同一组成，来源父的失效和旧快照不能改变真实根关系或改写物理行。</summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public void SharedNumber_UsesActualRootAndKeepsPhysicalSource(int marker)
    {
        var source = CreateProduct("SOURCE", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "SHARED");
        source.IsDeleted = true;
        var root = CreateProduct("ROOT", SupplyTypeEnums.采购, MaterialTypesEnum.成品, marker, "SHARED");
        var child = CreateProduct("CUSTOMER", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var line = CreateBom(source, child, 4, 2, 3);
        line.ParentSkuSnapshot = "历史来源父";
        line.ComponentSkuSnapshot = "历史子编码";
        line.Note = "历史备注";
        var before = System.Text.Json.JsonSerializer.Serialize(line);
        var plan = _planner.BuildPlan(CreateGross(false, root.Uid, 9), root, [line],
            new Dictionary<Guid, Product> { [child.Uid] = child }, 7);
        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.HasCount(2, plan.NetDemands);
        Assert.AreEqual(root.Uid, plan.NetDemands[0].ProductId);
        Assert.AreEqual(9m, plan.NetDemands[0].NetQty);
        Assert.AreEqual(6m, plan.NetDemands[1].NetQty);
        Assert.AreEqual(line.Uid, plan.NetDemands[1].BomLineId);
        Assert.AreEqual("ROOT", plan.NetDemands[1].ParentSkuSnapshot);
        Assert.AreEqual("ROOT/CUSTOMER", plan.NetDemands[1].BomPath);
        Assert.AreEqual("历史备注", plan.NetDemands[1].Remark);
        Assert.AreEqual(7, plan.NetDemands[1].DecompositionRevision);
        Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(line));
    }

    /// <summary>非来源共享父作为真实根时仍须拒绝自环，不能借来源父与子身份不同绕过校验。</summary>
    [TestMethod]
    public void SharedNumber_NonSourceRootSelfReferenceIsRejected()
    {
        var source = CreateProduct("SOURCE", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "SHARED");
        var root = CreateProduct("ROOT", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 3, "SHARED");
        var line = CreateBom(source, root, 1, 1, 1);
        var before = System.Text.Json.JsonSerializer.Serialize(line);
        var plan = _planner.BuildPlan(CreateGross(false, root.Uid, 3), root, [line],
            new Dictionary<Guid, Product> { [root.Uid] = root }, 1);
        Assert.IsFalse(plan.IsValid);
        Assert.AreEqual("INVALID_BOM_LINE", plan.ErrorCode);
        StringAssert.Contains(plan.ErrorMessage!, "不能相同");
        Assert.AreEqual(before, System.Text.Json.JsonSerializer.Serialize(line));
    }

    /// <summary>物理来源父恰为子产品时不构成真实根自环，来源字段不再被当作本次有效边。</summary>
    [TestMethod]
    public void PhysicalSourceIdentity_DoesNotReplaceActualRoot()
    {
        var root = CreateProduct("ROOT", SupplyTypeEnums.采购, MaterialTypesEnum.成品, 1, "SHARED");
        var child = CreateProduct("CHILD", SupplyTypeEnums.采购, MaterialTypesEnum.客供件);
        var line = CreateBom(root, child, 1, 1, 2);
        line.ParentProductId = child.Uid;
        var plan = _planner.BuildPlan(CreateGross(false, root.Uid, 8), root, [line],
            new Dictionary<Guid, Product> { [child.Uid] = child }, 1);
        Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
        Assert.AreEqual(4m, plan.NetDemands[1].NetQty);
        Assert.AreEqual(child.Uid, line.ParentProductId);
    }

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

    /// <summary>建立标准组成标记的物理行，父正标记仅表示有效共享引用，不作为组成版本。</summary>
    private static Bom CreateBom(Product parent, Product child, int line, int numerator, int denominator)
        => new()
        {
            Uid = Guid.NewGuid(),
            BomNo = parent.BomNo!,
            BomVersion = 1,
            ParentProductId = parent.Uid,
            ParentSkuSnapshot = parent.Sku,
            LineNumber = line,
            ComponentProductId = child.Uid,
            ComponentSkuSnapshot = child.Sku,
            Numerator = numerator,
            Denominator = denominator
        };
}
