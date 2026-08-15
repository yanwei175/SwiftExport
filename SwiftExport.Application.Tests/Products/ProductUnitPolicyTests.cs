using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Application.Tests.Products;

[TestClass]
public sealed class ProductUnitPolicyTests
{
    [TestMethod]
    public void Normalize_ShouldAcceptEmptyAndCanonicalizeEnabledDictionaryCode()
    {
        var empty = new ProductDto { Unit = " " };
        var selected = new ProductDto { Unit = " pcs " };

        var error = ProductUnitPolicy.Normalize(
        [
            new ProductUnitValidationItem(empty, null, 1),
            new ProductUnitValidationItem(selected, null, 2),
        ],
        [
            new SysDictData { DataNameEn = "PCS", IsEnabled = true },
        ]);

        Assert.IsNull(error);
        Assert.IsNull(empty.Unit);
        Assert.AreEqual("PCS", selected.Unit);
    }

    [TestMethod]
    public void Normalize_ShouldRejectNewUnknownUnitButPreserveUnchangedLegacyValue()
    {
        var unknown = new ProductDto { Unit = "OLD" };
        var error = ProductUnitPolicy.Normalize(
            [new ProductUnitValidationItem(unknown, null, 1)],
            []);

        Assert.IsNotNull(error);

        var legacy = new ProductDto { Unit = " OLD " };
        error = ProductUnitPolicy.Normalize(
            [new ProductUnitValidationItem(legacy, "OLD", 1)],
            []);

        Assert.IsNull(error);
        Assert.AreEqual("OLD", legacy.Unit);
    }

    [TestMethod]
    public void Normalize_ShouldRejectDuplicateEnabledCodesIgnoringCase()
    {
        var error = ProductUnitPolicy.Normalize(
            [new ProductUnitValidationItem(new ProductDto(), null, 1)],
        [
            new SysDictData { DataNameEn = "PCS", IsEnabled = true },
            new SysDictData { DataNameEn = "pcs", IsEnabled = true },
        ]);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "重复");
    }
}
