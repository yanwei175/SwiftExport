using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证中性产品管理批次的默认主图候选规则。</summary>
[TestClass]
public sealed class ProductNeutralManagerMainImagePolicyTests
{
    /// <summary>同一目标主型号出现多个默认候选时必须拒绝，避免依赖请求顺序决定结果。</summary>
    [TestMethod]
    public void MultipleWinnersForSameMaster_ShouldConflict()
    {
        var masterId = Guid.NewGuid();
        var writes = new[]
        {
            new ProductNeutralManagerWriteDto { ProductMasterId = masterId, IsMainImage = true },
            new ProductNeutralManagerWriteDto { ProductMasterId = masterId, IsMainImage = true }
        };

        Assert.IsTrue(ProductNeutralManagerMainImagePolicy.HasMultipleWinners(writes));
    }

    /// <summary>不同主型号可各有一个候选，并按稳定顺序返回需要替换的父标识。</summary>
    [TestMethod]
    public void OneWinnerPerMaster_ShouldReturnDistinctTargets()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var writes = new[]
        {
            new ProductNeutralManagerWriteDto { ProductMasterId = second, IsMainImage = true },
            new ProductNeutralManagerWriteDto { ProductMasterId = first, IsMainImage = true },
            new ProductNeutralManagerWriteDto { ProductMasterId = first, IsMainImage = false }
        };

        Assert.IsFalse(ProductNeutralManagerMainImagePolicy.HasMultipleWinners(writes));
        CollectionAssert.AreEqual(new[] { first, second },
            ProductNeutralManagerMainImagePolicy.GetWinnerMasterIds(writes).ToArray());
    }
}
