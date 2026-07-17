using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class PurchaseDraftWriteModelBuilderTests
{
    private readonly PurchaseDraftWriteModelBuilder _builder = new();

    [TestMethod]
    public void Build_NewDocumentCreatesAuthoritativeHeadLinesAndOneLinkPerContribution()
    {
        var executionRequestId = Guid.NewGuid();
        var purchaseOrderId = Guid.NewGuid();
        var document = CreateDocument();

        var model = _builder.Build(document, executionRequestId, purchaseOrderId, "PO2026070001", 1, createHead: true);

        Assert.IsNotNull(model.Head);
        Assert.AreEqual(executionRequestId, model.Head.ExecutionRequestId);
        Assert.AreEqual(document.SupplierId, model.Head.SupplierId);
        Assert.AreEqual(document.SourceUid, model.Head.DemandSourceUid);
        Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Active, model.Head.LifecycleStatus);
        Assert.AreEqual(ConfirmationStatusEnum.待确认, model.Head.ConfirmationStatus);
        Assert.AreEqual("CNY", model.Head.Currency);
        Assert.AreEqual(1, model.Lines.Count);
        Assert.AreEqual(2, model.Links.Count);
        Assert.AreEqual(model.Lines[0].Uid, model.Links[0].TargetLineId);
        Assert.AreEqual(model.Lines[0].Uid, model.Links[1].TargetLineId);
        Assert.AreEqual(model.Lines[0].QTY, model.Links.Sum(x => x.AllocatedQty));
        Assert.IsTrue(model.Links.All(x => x.ExecutionRequestId == executionRequestId));
    }

    [TestMethod]
    public void Build_AppendDoesNotCreateHeadAndStartsAfterLockedMaximumLineNumber()
    {
        var model = _builder.Build(
            CreateDocument(), Guid.NewGuid(), Guid.NewGuid(), "PO2026070002", 8, createHead: false);

        Assert.IsNull(model.Head);
        Assert.AreEqual(8, model.Lines.Single().LineNumber);
    }

    [TestMethod]
    public void Build_MergedLineDoesNotPretendToHaveSingleSourceLine()
    {
        var model = _builder.Build(
            CreateDocument(), Guid.NewGuid(), Guid.NewGuid(), "PO2026070003", 1, createHead: true);

        Assert.IsNull(model.Lines.Single().SourceLineId);
        Assert.IsNull(model.Lines.Single().SourceLineNumber);
        Assert.AreEqual(2, model.Links.Select(x => x.CommandItemId).Distinct().Count());
    }

    [TestMethod]
    public void Build_RejectsQuantityThatDoesNotEqualContributionSum()
    {
        var document = CreateDocument();
        document.Lines[0].PurchaseQty += 1m;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _builder.Build(document, Guid.NewGuid(), Guid.NewGuid(), "PO2026070004", 1, createHead: true));
    }

    private static PurchaseDraftPreviewDocumentDto CreateDocument()
    {
        var sourceUid = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var first = new PurchaseDraftSourceContributionDto
        {
            CommandItemId = Guid.NewGuid(),
            AllocationId = Guid.NewGuid(),
            AllocationVersion = 1,
            NetDemandId = Guid.NewGuid(),
            SourceLineId = Guid.NewGuid(),
            SourceLineNumber = 1,
            ExpectedRemainingQty = 3m,
            PurchaseQty = 3m,
            DemandRequiredDate = new DateTime(2026, 8, 12),
            PlannedDeliveryDate = new DateTime(2026, 8, 10)
        };
        var second = new PurchaseDraftSourceContributionDto
        {
            CommandItemId = Guid.NewGuid(),
            AllocationId = Guid.NewGuid(),
            AllocationVersion = 1,
            NetDemandId = Guid.NewGuid(),
            SourceLineId = Guid.NewGuid(),
            SourceLineNumber = 2,
            ExpectedRemainingQty = 2m,
            PurchaseQty = 2m,
            DemandRequiredDate = new DateTime(2026, 8, 13),
            PlannedDeliveryDate = new DateTime(2026, 8, 10)
        };
        return new PurchaseDraftPreviewDocumentDto
        {
            SourceType = "SO",
            SourceUid = sourceUid,
            SourceNo = "SO001",
            SupplierId = supplierId,
            SupplierCode = "SUP001",
            SupplierName = "供应商一",
            Currency = CurrencyEnum.CNY,
            SourceLineCount = 2,
            PreviewLineCount = 1,
            TotalQty = 5m,
            EarliestPlannedDeliveryDate = new DateTime(2026, 8, 10),
            Lines = new List<PurchaseDraftPreviewLineDto>
            {
                new()
                {
                    ProductId = Guid.NewGuid(),
                    Sku = "SKU001",
                    LinkSku = "LINK001",
                    Description = "测试产品",
                    MaterialType = "成品",
                    SupplierSku = "S-SKU001",
                    UnitPrice = 10m,
                    TaxPrice = 11.3m,
                    PurchaseQty = 5m,
                    PlannedDeliveryDate = new DateTime(2026, 8, 10),
                    Contributions = new List<PurchaseDraftSourceContributionDto> { first, second }
                }
            }
        };
    }
}
