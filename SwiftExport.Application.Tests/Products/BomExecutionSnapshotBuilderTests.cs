using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products.Execution;
using SwiftExport.Contracts.DTOs.Products.Execution;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Application.Tests.Products;

/// <summary>验证独立采购执行结构的完整性、路径保留、精确累计比例及指纹稳定性。</summary>
[TestClass]
public sealed class BomExecutionSnapshotBuilderTests
{
    /// <summary>无 BOM 与损坏的正版本 BOM 必须区分，根字段变化同样影响无 BOM 指纹。</summary>
    [TestMethod]
    public void NoBom_IsExplicitAndRootUnitChangesFingerprint()
    {
        var root = Product("ROOT");
        var first = Build(root, [root], []);
        Assert.IsFalse(first.HasBom);
        Assert.AreEqual(0, first.Nodes.Count);
        Assert.AreEqual(64, first.Fingerprint.Length);
        root.Unit = "SET";
        var next = Build(root, [root], []);
        Assert.AreNotEqual(first.Fingerprint, next.Fingerprint);
        root.BomVersion = 1;
        root.BomNo = "ROOT-BOM";
        Reject(() => Build(root, [root], []), "缺少组成行");
    }

    /// <summary>共享组件在不同分支保留两个路径，同父重复则为非法组成。</summary>
    [TestMethod]
    public void SharedComponent_KeepsPathsAndRejectsSameParentDuplicates()
    {
        var root = Product("ROOT", true);
        var left = Product("LEFT", true);
        var right = Product("RIGHT", true);
        var shared = Product("SHARED");
        shared.MaterialType = MaterialTypesEnum.客供件;
        var lines = new[] { Line(root, left), Line(root, right), Line(left, shared), Line(right, shared) };
        var first = Build(root, [root, left, right, shared], lines);
        var reused = first.Nodes.Where(x => x.ComponentProductId == shared.Uid).ToArray();
        Assert.AreEqual(2, reused.Length);
        Assert.AreNotEqual(reused[0].Path, reused[1].Path);
        Assert.IsTrue(reused.All(x => x.IsCustomerSupplied));
        Assert.IsTrue(reused.All(x => x.Path.Split('/').Length == 2));
        var shuffled = Build(root, [shared, right, left, root], lines.Reverse().ToArray());
        Assert.AreEqual(first.Fingerprint, shuffled.Fingerprint);
        Reject(() => Build(root, [root, left, right, shared], [.. lines, Line(root, left)]), "重复组件");
    }

    /// <summary>子层使用原始分数累计而非已舍入父节点，避免三分之一乘三错误成为0.9999。</summary>
    [TestMethod]
    public void CumulativeRatio_DoesNotRoundIntermediateFactors()
    {
        var root = Product("ROOT", true);
        var middle = Product("MIDDLE", true);
        var leaf = Product("LEAF");
        var snapshot = Build(root, [root, middle, leaf], [Line(root, middle, 1, 3), Line(middle, leaf, 3, 1)]);
        Assert.AreEqual(0.3333m, snapshot.Nodes.Single(x => x.ComponentProductId == middle.Uid).CumulativeRatio);
        Assert.AreEqual(1m, snapshot.Nodes.Single(x => x.ComponentProductId == leaf.Uid).CumulativeRatio);
        var midpoint = Build(root, [root, leaf], [Line(root, leaf, 1, 20000)]);
        Assert.AreEqual(0.0001m, midpoint.Nodes.Single().CumulativeRatio);
    }

    /// <summary>长链的大整数因子仍精确累计，不因中间分子超过decimal范围而报错。</summary>
    [TestMethod]
    public void LargeRawFactors_AreReducedWithoutDecimalOverflow()
    {
        var products = Enumerable.Range(0, 40).Select(i => Product($"P{i}", i < 39)).ToArray();
        var lines = Enumerable.Range(0, 39).Select(i => Line(products[i], products[i + 1], 100000, 99999)).ToArray();
        var snapshot = Build(products[0], products, lines);
        Assert.AreEqual(39, snapshot.Nodes.Count);
        Assert.AreEqual(1.0004m, snapshot.Nodes.Single(x => x.ComponentProductId == products[^1].Uid).CumulativeRatio);
    }

    /// <summary>超过数据库精度和正值舍入为零均须拒绝，不截断或保存零消耗。</summary>
    [TestMethod]
    public void RatioOverflowAndUnderflow_AreRejected()
    {
        var root = Product("ROOT", true);
        var middle = Product("MIDDLE", true);
        var leaf = Product("LEAF");
        Reject(() => Build(root, [root, leaf], [Line(root, leaf, 1, int.MaxValue)]), "精度范围");
        Reject(() => Build(root, [root, middle, leaf],
            [Line(root, middle, int.MaxValue, 1), Line(middle, leaf, int.MaxValue, 1)]), "精度范围");
    }

    /// <summary>循环、组件缺失和非法比例必须整体失败，不能返回部分执行节点。</summary>
    [TestMethod]
    public void CorruptedGraph_CycleMissingComponentAndInvalidFactorAreRejected()
    {
        var root = Product("ROOT", true);
        var child = Product("CHILD", true);
        Reject(() => Build(root, [root, child], [Line(root, child), Line(child, root)]), "循环");
        Reject(() => Build(root, [root], [Line(root, child)]), "不存在");
        Reject(() => Build(root, [root, child], [Line(root, child, 1, 0)]), "比例无效");
        Reject(() => Build(root, [root, child], [Line(root, child, 0, 1)]), "比例无效");
    }

    /// <summary>启停和单位资格影响新执行；客供件不能有BOM，虚拟件可保留为结构节点。</summary>
    [TestMethod]
    public void ProductEligibilityAndCustomerSuppliedTerminalRules_AreEnforced()
    {
        var root = Product("ROOT", true);
        var child = Product("CHILD");
        var line = Line(root, child);
        child.IsActive = false;
        Reject(() => Build(root, [root, child], [line]), "已停用");
        child.IsActive = true;
        child.Unit = "UNKNOWN";
        Reject(() => Build(root, [root, child], [line]), "基础单位");
        child.Unit = "PCS";
        child.MaterialType = MaterialTypesEnum.虚拟件;
        Assert.AreEqual(MaterialTypesEnum.虚拟件, Build(root, [root, child], [line]).Nodes.Single().MaterialType);
        child.MaterialType = MaterialTypesEnum.客供件;
        child.BomVersion = 1;
        child.BomNo = "INVALID";
        Reject(() => Build(root, [root, child], [line]), "终端物料");
        child.BomVersion = 0;
        child.BomNo = null;
        child.SupplyType = SupplyTypeEnums.生产;
        Reject(() => Build(root, [root, child], [line]), "采购型终端");
    }

    /// <summary>单位停用或重复启用代码不能被无BOM快照绕过，终端残留组成也必须显式报错。</summary>
    [TestMethod]
    public void DisabledUnitAndDanglingTerminalComposition_AreRejected()
    {
        var root = Product("ROOT");
        var products = new Dictionary<Guid, Product> { [root.Uid] = root };
        Reject(() => BomExecutionSnapshotBuilder.Build(root.Uid, products, [],
            [new SysDictData { DictCode = "Unit", DataNameEn = "PCS", IsEnabled = false }]), "基础单位");
        Reject(() => BomExecutionSnapshotBuilder.Build(root.Uid, products, [],
            [new SysDictData { DictCode = "Unit", DataNameEn = "PCS", IsEnabled = true },
             new SysDictData { DictCode = "Unit", DataNameEn = "pcs", IsEnabled = true }]), "重复");
        var child = Product("CHILD");
        var dangling = Line(root, child);
        dangling.BomVersion = 1;
        dangling.BomNo = "DANGLING";
        Reject(() => Build(root, [root, child], [dangling]), "声明无 BOM");
        root.MaterialType = MaterialTypesEnum.客供件;
        Reject(() => Build(root, [root, child], [dangling]), "终端物料");
    }

    /// <summary>取消请求应传播原取消异常，不能转成空结构或继续生成执行快照。</summary>
    [TestMethod]
    public void CancelledBuild_DoesNotReturnSnapshot()
    {
        var root = Product("ROOT");
        Assert.ThrowsExactly<OperationCanceledException>(() => BomExecutionSnapshotBuilder.Build(root.Uid,
            new Dictionary<Guid, Product> { [root.Uid] = root }, [], [], new CancellationToken(true)));
    }

    /// <summary>执行相关单位或因子变化必须使旧确认上下文失效，已返回快照内容保持不变。</summary>
    [TestMethod]
    public void ExecutionChanges_InvalidateFingerprintWithoutChangingPriorSnapshot()
    {
        var root = Product("ROOT", true);
        var child = Product("CHILD");
        var line = Line(root, child);
        var first = Build(root, [root, child], [line]);
        line.Numerator = 2;
        var second = Build(root, [root, child], [line]);
        Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
        Assert.AreEqual(1m, first.Nodes.Single().CumulativeRatio);
        child.Unit = "SET";
        Assert.AreNotEqual(second.Fingerprint, Build(root, [root, child], [line]).Fingerprint);
    }

    /// <summary>路径超出持久化列上限时整次失败，不能截断后形成路径碰撞。</summary>
    [TestMethod]
    public void ExcessiveDepth_IsRejectedInsteadOfTruncatingPath()
    {
        var products = Enumerable.Range(0, 123).Select(i => Product($"P{i}", i < 122)).ToArray();
        var lines = Enumerable.Range(0, 122).Select(i => Line(products[i], products[i + 1])).ToArray();
        Reject(() => Build(products[0], products, lines), "安全上限");
    }

    private static Product Product(string sku, bool hasBom = false) => new()
    {
        Sku = sku, Unit = "PCS", IsActive = true, MaterialType = MaterialTypesEnum.零件,
        SupplyType = SupplyTypeEnums.采购, BomVersion = hasBom ? 1 : 0, BomNo = hasBom ? $"{sku}-BOM" : null
    };

    private static Bom Line(Product parent, Product child, int numerator = 1, int denominator = 1) => new()
    {
        ParentProductId = parent.Uid, ComponentProductId = child.Uid,
        ParentSkuSnapshot = parent.Sku, ComponentSkuSnapshot = child.Sku,
        BomNo = parent.BomNo!, BomVersion = parent.BomVersion, Numerator = numerator, Denominator = denominator
    };

    private static BomExecutionSnapshotDto Build(Product root, Product[] products, Bom[] lines) =>
        BomExecutionSnapshotBuilder.Build(root.Uid, products.ToDictionary(x => x.Uid), lines,
            [new SysDictData { DictCode = "Unit", DataNameEn = "PCS", IsEnabled = true },
             new SysDictData { DictCode = "Unit", DataNameEn = "SET", IsEnabled = true }]);

    /// <summary>只接受明确的业务结构异常，测试中意外空引用不能当作预期阻断。</summary>
    private static void Reject(Action action, string message)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(action);
        StringAssert.Contains(error.Message, message);
    }
}
