using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Application.Tests.Products;

/// <summary>共享结构核心校验回归，保留重复、自环、间接环和空组成断言。</summary>
[TestClass]
public sealed class BomStructureValidatorTests
{
    private readonly SharedBomStructureValidator _validator = new();

    /// <summary>合法组成通过共享结构校验。</summary>
    [TestMethod]
    public void ValidStructure_Passes()
    {
        var parent = Product("A", "BOM-A");
        var child = Product("B");
        var error = Validate(
            [Line(parent, child, 1)],
            [parent.Uid],
            Products(parent, child));

        Assert.IsNull(error);
    }

    /// <summary>同组重复组件仍被拒绝。</summary>
    [TestMethod]
    public void DuplicateComponent_IsRejected()
    {
        var parent = Product("A", "BOM-A");
        var child = Product("B");
        var error = Validate(
            [Line(parent, child, 1), Line(parent, child, 2)],
            [parent.Uid],
            Products(parent, child));

        StringAssert.Contains(error, "重复");
    }

    /// <summary>产品不能成为自身组成。</summary>
    [TestMethod]
    public void ParentAsOwnComponent_IsRejected()
    {
        var parent = Product("A", "BOM-A");
        var error = Validate(
            [Line(parent, parent, 1)],
            [parent.Uid],
            Products(parent));

        StringAssert.Contains(error, "不能相同");
    }

    /// <summary>跨多个组的间接环仍被拒绝。</summary>
    [TestMethod]
    public void IndirectCycle_IsRejected()
    {
        var a = Product("A", "BOM-A");
        var b = Product("B", "BOM-B");
        var c = Product("C", "BOM-C");
        var error = Validate(
            [Line(a, b, 1), Line(b, c, 1), Line(c, a, 1)],
            [a.Uid],
            Products(a, b, c));

        StringAssert.Contains(error, "循环依赖");
    }

    /// <summary>被引用的组成不能被保存为空。</summary>
    [TestMethod]
    public void EmptyCurrentVersion_IsRejected()
    {
        var parent = Product("A", "BOM-A");
        var error = Validate([], [parent.Uid], Products(parent));

        StringAssert.Contains(error, "至少需要保留一条");
    }

    /// <summary>为固定 ASCII 场景显式构造组事实，不模拟数据库 Unicode 排序规则。</summary>
    private string? Validate(IReadOnlyList<Bom> lines, IReadOnlyCollection<Guid> affected,
        IReadOnlyDictionary<Guid, Product> products)
    {
        var groups = products.Values.Where(product => product.BomVersion > 0)
            .GroupBy(product => product.BomNo).Select((group, index) => new SharedBomGroup
            {
                GroupKey = index, BomNo = group.Key!, Products = group.ToArray(),
                Lines = lines.Where(line => line.BomNo == group.Key).ToArray()
            }).ToArray();
        return _validator.Validate(groups,
            groups.Where(group => group.Products.Any(product => affected.Contains(product.Uid)))
                .Select(group => group.GroupKey).ToArray(), products);
    }

    private static Product Product(string sku, string? bomNo = null) => new()
    {
        Uid = Guid.NewGuid(),
        Sku = sku,
        BomNo = bomNo,
        BomVersion = bomNo is null ? 0 : 1,
        SupplyType = "采购"
    };

    private static Bom Line(Product parent, Product child, int lineNumber) => new()
    {
        Uid = Guid.NewGuid(),
        BomNo = parent.BomNo!,
        BomVersion = parent.BomVersion,
        ParentProductId = parent.Uid,
        ParentSkuSnapshot = parent.Sku,
        LineNumber = lineNumber,
        ComponentProductId = child.Uid,
        ComponentSkuSnapshot = child.Sku,
        Numerator = 1,
        Denominator = 1
    };

    private static IReadOnlyDictionary<Guid, Product> Products(params Product[] products) =>
        products.ToDictionary(x => x.Uid);
}
