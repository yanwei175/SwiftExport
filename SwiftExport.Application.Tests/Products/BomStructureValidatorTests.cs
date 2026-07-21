using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.Products;

[TestClass]
public sealed class BomStructureValidatorTests
{
    private readonly BomStructureValidator _validator = new();

    [TestMethod]
    public void ValidStructure_Passes()
    {
        var parent = Product("A", "BOM-A");
        var child = Product("B");
        var error = _validator.Validate(
            [Line(parent, child, 1)],
            [parent.Uid],
            Products(parent, child));

        Assert.IsNull(error);
    }

    [TestMethod]
    public void DuplicateComponent_IsRejected()
    {
        var parent = Product("A", "BOM-A");
        var child = Product("B");
        var error = _validator.Validate(
            [Line(parent, child, 1), Line(parent, child, 2)],
            [parent.Uid],
            Products(parent, child));

        StringAssert.Contains(error, "重复");
    }

    [TestMethod]
    public void ParentAsOwnComponent_IsRejected()
    {
        var parent = Product("A", "BOM-A");
        var error = _validator.Validate(
            [Line(parent, parent, 1)],
            [parent.Uid],
            Products(parent));

        StringAssert.Contains(error, "不能相同");
    }

    [TestMethod]
    public void IndirectCycle_IsRejected()
    {
        var a = Product("A", "BOM-A");
        var b = Product("B", "BOM-B");
        var c = Product("C", "BOM-C");
        var error = _validator.Validate(
            [Line(a, b, 1), Line(b, c, 1), Line(c, a, 1)],
            [a.Uid],
            Products(a, b));

        StringAssert.Contains(error, "循环依赖");
    }

    [TestMethod]
    public void EmptyCurrentVersion_IsRejected()
    {
        var parent = Product("A", "BOM-A");
        var error = _validator.Validate([], [parent.Uid], Products(parent));

        StringAssert.Contains(error, "至少需要保留一条");
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
