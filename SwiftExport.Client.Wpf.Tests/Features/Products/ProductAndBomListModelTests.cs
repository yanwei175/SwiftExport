using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.ProductList.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductAndBomListModelTests
{
    [TestMethod]
    public void ProductListModel_ShouldReuseBaseStateAndBuildBatchRequest()
    {
        var list = new ProductListModel();
        list.LoadFromDtos([new ProductDto { Uid = Guid.NewGuid(), Version = 3, Sku = "P-1" }]);
        var existing = list.Items.Single();
        existing.DescriptionZh = "修改";
        var added = list.AddNewProduct();
        added.Sku = "P-2";

        var request = list.CreateSaveRequest();

        Assert.AreEqual(1, request.CreateItems.Count);
        Assert.AreEqual("P-2", request.CreateItems[0].Data.Sku);
        Assert.AreEqual(1, request.UpdateItems.Count);
        Assert.AreEqual(existing.Uid, request.UpdateItems[0].Uid);
        Assert.IsTrue(list.HasChanges);

        list.RejectChanges();
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(string.Empty, list.Items[0].DescriptionZh);
        Assert.IsFalse(list.HasChanges);
    }

    [TestMethod]
    public void BomListModel_ShouldReuseBaseDeletePoolAndBuildBatchRequest()
    {
        var persistedId = Guid.NewGuid();
        var list = new BomListModel();
        list.LoadFromDtos([new BomDto
        {
            Uid = persistedId,
            Version = 2,
            BomNo = "B-1",
            SubSkuId = Guid.NewGuid(),
            SubSku = "SUB-1",
            Numerator = 1,
            Denominator = 1,
        }]);
        var persisted = list.Items.Single();
        list.Remove(persisted);
        var added = list.AddNewItem("B-1");
        added.SubSkuId = Guid.NewGuid();
        added.SubSku = "SUB-2";

        var request = list.CreateSaveRequest();

        Assert.AreEqual(1, request.CreateItems.Count);
        Assert.AreEqual("B-1", request.CreateItems[0].Data.BomNo);
        Assert.AreEqual(1, request.DeleteItems.Count);
        Assert.AreEqual(persistedId, request.DeleteItems[0].Uid);

        list.AcceptChanges();
        Assert.AreEqual(1, list.Items.Count);
        Assert.IsFalse(list.HasChanges);
    }
}
