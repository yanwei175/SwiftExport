using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;

[TestClass]
public sealed class GrossDemandManualEditModelTests
{
    [TestMethod]
    public void ResetAndAcceptChanges_ShouldMaintainExplicitDirtyBaseline()
    {
        var model = new GrossDemandManualEditModel();
        model.ResetForCreate();

        Assert.IsFalse(model.IsDirty);

        model.Description = "临时补货";
        Assert.IsTrue(model.IsDirty);

        model.AcceptChanges();
        Assert.IsFalse(model.IsDirty);
    }

    [TestMethod]
    public void ValidateForSave_ShouldRequireProductForStandardAndDescriptionForNonStandard()
    {
        var model = new GrossDemandManualEditModel();
        model.ResetForCreate();
        model.Description = "需求";
        model.DemandQty = 2m;

        Assert.IsFalse(model.ValidateForSave(out var standardMessage));
        StringAssert.Contains(standardMessage, "选择产品");

        model.IsNonStandard = true;
        Assert.IsTrue(model.ValidateForSave(out _));
        Assert.AreEqual(string.Empty, model.ToCreateRequest().Sku);

        model.Description = string.Empty;
        Assert.IsFalse(model.ValidateForSave(out var nonStandardMessage));
        StringAssert.Contains(nonStandardMessage, "描述");
    }

    [TestMethod]
    public void ApplyProduct_ShouldFreezeProductSnapshotAndCreateMatchingRequest()
    {
        var productId = Guid.NewGuid();
        var model = new GrossDemandManualEditModel();
        model.ResetForCreate();
        model.ApplyProduct(new ProductLookupDto
        {
            Uid = productId,
            Sku = "A-100",
            LinkSku = "LINK-A",
            DescriptionEn = "Product A",
            Finish = "Black",
            FeaturesEn = "100mm",
            MainImage = "a.jpg",
        });
        model.DemandQty = 5m;

        var request = model.ToCreateRequest();

        Assert.AreEqual(productId, request.ProductId);
        Assert.AreEqual("A-100", request.Sku);
        Assert.AreEqual("Product A", request.Description);
        Assert.AreEqual("Black", request.Color);
        Assert.AreEqual("100mm", request.Specification);
        Assert.AreEqual(5m, request.DemandQty);
    }

    [TestMethod]
    public void Load_ShouldAcceptServerVersionAsNewBaseline()
    {
        var uid = Guid.NewGuid();
        var model = new GrossDemandManualEditModel();

        model.Load(new GrossDemandDetailDto
        {
            Uid = uid,
            Version = 7,
            SourceNo = "MD-0007",
            IsNonStandard = true,
            Sku = "TMP-007",
            Description = "补货",
            DemandQty = 3m,
        });

        Assert.AreEqual(uid, model.Uid);
        Assert.AreEqual(7, model.Version);
        Assert.IsFalse(model.IsNew);
        Assert.IsFalse(model.IsDirty);
    }
}
