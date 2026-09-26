using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning.Policies;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class CustomerSuppliedMaterialPolicyTests
{
    [TestMethod]
    public void CustomerSuppliedProduct_MustBePurchasedAndTerminal()
    {
        var product = CreateCustomerSupplied(SupplyTypeEnums.生产);

        Assert.IsNotNull(CustomerSuppliedMaterialPolicy.ValidateProduct(product, false));

        product.SupplyType = SupplyTypeEnums.采购;
        product.BomVersion = 1;
        product.BomNo = "BOM-B";
        Assert.IsNotNull(CustomerSuppliedMaterialPolicy.ValidateProduct(product, false));

        product.BomVersion = 0;
        product.BomNo = null;
        Assert.IsNotNull(CustomerSuppliedMaterialPolicy.ValidateProduct(product, true));
        Assert.IsNull(CustomerSuppliedMaterialPolicy.ValidateProduct(product, false));
    }

    [TestMethod]
    public void CustomerSuppliedProduct_CannotBeBomParent()
    {
        var product = CreateCustomerSupplied(SupplyTypeEnums.采购);

        Assert.IsNotNull(CustomerSuppliedMaterialPolicy.ValidateBomParent(product));
    }

    private static Product CreateCustomerSupplied(string supplyType) => new()
    {
        Uid = Guid.NewGuid(),
        Sku = "B",
        MaterialType = MaterialTypesEnum.客供件,
        SupplyType = supplyType,
        BomVersion = 0
    };
}
