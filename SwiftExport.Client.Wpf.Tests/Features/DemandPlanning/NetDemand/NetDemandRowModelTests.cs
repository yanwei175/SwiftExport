using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandRowModelTests
{
    [TestMethod]
    public void FromDto_ShouldKeepMaterialAndPurchaseFactsSeparateFromClientSelection()
    {
        var dto = new MaterialDemandManagerItemDto
        {
            Uid = Guid.NewGuid(),
            SourceType = "SO",
            SourceNo = "SO-1001",
            Sku = "B-100",
            RootSku = "A-100",
            IsCustomerSupplied = true,
            NetQty = 20m,
            AssociatedPurchaseQty = 8m,
        };

        var row = MaterialDemandManagerRowModel.FromDto(dto);

        Assert.AreEqual("客供件", row.DemandRoleText);
        Assert.AreEqual(8m, row.AssociatedPurchaseQty);
        Assert.AreEqual(" ↳ B-100", row.DisplaySku);
        Assert.IsFalse(row.IsChecked);
        Assert.IsFalse(dto.GetType().GetProperties().Any(property => property.Name == nameof(row.IsChecked)));
    }

    [TestMethod]
    public void SuggestedSupplier_EditRejectAndAccept_ShouldPreserveIndependentPurchaseAndDeliverySuggestions()
    {
        var originalSupplierId = Guid.NewGuid();
        var deliverySupplierId = Guid.NewGuid();
        var row = MaterialDemandManagerRowModel.FromDto(new MaterialDemandManagerItemDto
        {
            Uid = Guid.NewGuid(),
            Version = 3,
            IsCustomerSupplied = true,
            SuggestedPurchaseSupplierId = originalSupplierId,
            SuggestedPurchaseSupplierCode = "SELF",
            SuggestedPurchaseSupplierName = "自身采购方",
            SuggestedDeliverySupplierId = deliverySupplierId,
            SuggestedDeliverySupplierCode = "PARENT",
            SuggestedDeliverySupplierName = "父件采购方"
        });
        var changedSupplierId = Guid.NewGuid();

        row.ApplySuggestedSupplier(new ProcurementSupplierCandidateDto
        {
            Uid = changedSupplierId,
            SupplierCode = "CHANGED",
            SupplierName = "修改后的采购建议"
        });

        Assert.IsTrue(row.IsSuggestionDirty);
        Assert.AreEqual(changedSupplierId, row.SuggestedPurchaseSupplierId);
        Assert.AreEqual(deliverySupplierId, row.SuggestedDeliverySupplierId);
        Assert.AreEqual("父件采购方", row.SuggestedDeliverySupplierDisplay);
        Assert.AreEqual("PARENT", row.SuggestedDeliverySupplierCode);

        row.RejectSuggestedSupplierChanges();
        Assert.IsFalse(row.IsSuggestionDirty);
        Assert.AreEqual(originalSupplierId, row.SuggestedPurchaseSupplierId);

        row.ClearSuggestedSupplier();
        row.AcceptSuggestedSupplierUpdate(new UpdateSuggestedPurchaseSupplierResult
        {
            NetDemandId = row.Uid,
            Version = 4,
            SuggestedPurchaseSupplierId = null,
            SuggestedPurchaseSupplierCode = string.Empty,
            SuggestedPurchaseSupplierName = string.Empty
        });

        Assert.IsFalse(row.IsSuggestionDirty);
        Assert.IsNull(row.SuggestedPurchaseSupplierId);
        Assert.AreEqual(4, row.Version);
    }
}
