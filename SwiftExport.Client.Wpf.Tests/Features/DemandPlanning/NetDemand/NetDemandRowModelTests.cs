using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandRowModelTests
{
    [TestMethod]
    public void FromDto_ShouldKeepMaterialAndPurchaseFactsSeparateFromClientSelection()
    {
        var dto = new MaterialDemandWorkspaceItemDto
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

        var row = MaterialDemandWorkspaceRowModel.FromDto(dto);

        Assert.AreEqual("客供件", row.DemandRoleText);
        Assert.AreEqual(8m, row.AssociatedPurchaseQty);
        StringAssert.StartsWith(row.DisplaySku, "    ↳");
        Assert.IsFalse(row.IsChecked);
        Assert.IsFalse(dto.GetType().GetProperties().Any(property => property.Name == nameof(row.IsChecked)));
    }
}
