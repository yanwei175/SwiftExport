using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Runtime.CompilerServices;
using SwiftExport.Application.Services.Products;
using SwiftExport.Application.Services.Products.Catalog.DefaultSupplier;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Products;

[TestClass]
public sealed class ProductDefaultSupplierPolicyTests
{
    [TestMethod]
    public void Normalize_ShouldUseCanonicalSupplierCodeAndClearLegacyCodeForNullId()
    {
        var supplierId = Guid.NewGuid();
        var selected = new ProductDto
        {
            DefaultSupplierId = supplierId,
            DefaultSupplierCode = "CLIENT-CODE",
        };
        var cleared = new ProductDto
        {
            DefaultSupplierId = null,
            DefaultSupplierCode = "STALE-CODE",
        };

        var error = ProductDefaultSupplierPolicy.Normalize(
            [selected, cleared],
            [new Supplier
            {
                Uid = supplierId,
                SupplierCode = "SERVER-CODE",
                SupplierName = "服务端供应商",
                DocRelateNo = string.Empty,
            }]);

        Assert.IsNull(error);
        Assert.AreEqual("SERVER-CODE", selected.DefaultSupplierCode);
        Assert.AreEqual(string.Empty, cleared.DefaultSupplierCode);
    }

    [TestMethod]
    public void Normalize_ShouldRejectMissingOrInactiveSupplierReference()
    {
        var missingId = Guid.NewGuid();
        var product = new ProductDto { DefaultSupplierId = missingId };

        var error = ProductDefaultSupplierPolicy.Normalize([product], []);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, missingId.ToString());
    }

    [TestMethod]
    public void GetReferencedSupplierIds_ShouldIgnoreNullAndDeduplicate()
    {
        var supplierId = Guid.NewGuid();

        var ids = ProductDefaultSupplierPolicy.GetReferencedSupplierIds(
        [
            new ProductDto(),
            new ProductDto { DefaultSupplierId = supplierId },
            new ProductDto { DefaultSupplierId = supplierId },
        ]);

        Assert.HasCount(1, ids);
        Assert.AreEqual(supplierId, ids[0]);
    }

    [TestMethod]
    public void ProductRepository_ShouldPersistSupplierIdentityWithoutLegacyCodes()
    {
        var repository = (ProductRepository)RuntimeHelpers.GetUninitializedObject(typeof(ProductRepository));
        var insertProperties = ReadWriteProperties(repository, "InsertProperties");
        var updateProperties = ReadWriteProperties(repository, "UpdateProperties");

        CollectionAssert.Contains(insertProperties, nameof(Product.DefaultSupplierId));
        CollectionAssert.Contains(updateProperties, nameof(Product.DefaultSupplierId));
        CollectionAssert.Contains(insertProperties, nameof(Product.Unit));
        CollectionAssert.Contains(updateProperties, nameof(Product.Unit));
        CollectionAssert.DoesNotContain(insertProperties, "DefaultSupplierCode");
        CollectionAssert.DoesNotContain(updateProperties, "DefaultSupplierCode");
        CollectionAssert.DoesNotContain(insertProperties, "DefaultSupplierSku");
        CollectionAssert.DoesNotContain(updateProperties, "DefaultSupplierSku");
    }

    private static List<string> ReadWriteProperties(ProductRepository repository, string propertyName)
    {
        var property = typeof(ProductRepository).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(property);
        var value = property.GetValue(repository) as IReadOnlyList<string>;
        Assert.IsNotNull(value);
        return value.ToList();
    }
}
