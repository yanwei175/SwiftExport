using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料建立时的推荐采购供应商只来自各行自己的有效产品默认值。</summary>
[TestClass]
public sealed class MaterialDemandSuggestedSupplierInitializerTests
{
    private readonly MaterialDemandSuggestedSupplierInitializer _initializer = new();

    [TestMethod]
    public void Apply_ShouldKeepParentAndCustomerSuppliedOwnSuggestionsIndependent()
    {
        var parentSupplier = CreateSupplier("PARENT");
        var childSupplier = CreateSupplier("CHILD");
        var parentProduct = CreateProduct(parentSupplier.Uid);
        var childProduct = CreateProduct(childSupplier.Uid);
        var parent = CreateDemand(parentProduct.Uid);
        var child = CreateDemand(childProduct.Uid);
        child.DeliveryTargetNetDemandId = parent.Uid;

        _initializer.Apply(
            [parent, child],
            new Dictionary<Guid, Product>
            {
                [parentProduct.Uid] = parentProduct,
                [childProduct.Uid] = childProduct
            },
            new Dictionary<Guid, Supplier>
            {
                [parentSupplier.Uid] = parentSupplier,
                [childSupplier.Uid] = childSupplier
            });

        Assert.AreEqual(parentSupplier.Uid, parent.SuggestedPurchaseSupplierId);
        Assert.AreEqual(childSupplier.Uid, child.SuggestedPurchaseSupplierId);
    }

    [TestMethod]
    public void Apply_ShouldLeaveMissingProductOrInvalidDefaultSupplierEmpty()
    {
        var invalidSupplierId = Guid.NewGuid();
        var product = CreateProduct(invalidSupplierId);
        var invalidDefault = CreateDemand(product.Uid);
        var nonStandard = CreateDemand(null);

        _initializer.Apply(
            [invalidDefault, nonStandard],
            new Dictionary<Guid, Product> { [product.Uid] = product },
            new Dictionary<Guid, Supplier>());

        Assert.IsNull(invalidDefault.SuggestedPurchaseSupplierId);
        Assert.IsNull(nonStandard.SuggestedPurchaseSupplierId);
    }

    private static NetDemand CreateDemand(Guid? productId) => new()
    {
        Uid = Guid.NewGuid(),
        ProductId = productId,
        SuggestedPurchaseSupplierId = Guid.NewGuid()
    };

    private static Product CreateProduct(Guid? supplierId) => new()
    {
        Uid = Guid.NewGuid(),
        DefaultSupplierId = supplierId
    };

    private static Supplier CreateSupplier(string code) => new()
    {
        Uid = Guid.NewGuid(),
        SupplierCode = code,
        SupplierName = code
    };
}
