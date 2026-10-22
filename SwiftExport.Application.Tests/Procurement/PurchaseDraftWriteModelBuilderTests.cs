using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.Procurement;

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

        document.Header = new PurchaseDraftPreviewHeaderDto
        {
            InletHoseType = "软管 A",
            AeratorType = "起泡器 B",
            SalesPerson = "业务员甲",
            PurchasePerson = "采购员乙",
            InstructionManualType = "说明书 C",
            Remark = "采购备注",
            BoNo = "CINV-S",
            CustomerPoNo = "CUSTOMER-PO",
            Currency = "USD",
            DiscountRate = 0.95m,
            DiscountRemark = "倍率说明"
        };
        var model = _builder.Build(document, executionRequestId, purchaseOrderId, "PO2026070001", 1, createHead: true);

        Assert.IsNotNull(model.Head);
        Assert.AreEqual(executionRequestId, model.Head.ExecutionRequestId);
        Assert.AreEqual(document.SupplierId, model.Head.SupplierId);
        Assert.AreEqual(document.SourceUid, model.Head.DemandSourceUid);
        Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Active, model.Head.LifecycleStatus);
        Assert.AreEqual(ConfirmationStatusEnum.待确认, model.Head.ConfirmationStatus);
        Assert.AreEqual("USD", model.Head.Currency);
        Assert.AreEqual("CINV-S", model.Head.BoNo);
        Assert.AreEqual("CUSTOMER-PO", model.Head.CustomerPoNo);
        Assert.AreEqual(0.95m, model.Head.DiscountRate);
        Assert.IsNull(model.Head.DiscountAmount);
        Assert.AreEqual("倍率说明", model.Head.ZheKouBeiZhu);
        Assert.AreEqual(document.CustomerId, model.Head.CustomerId);
        Assert.AreEqual(document.CustomerCode, model.Head.CustomerCode);
        Assert.AreEqual(document.CustomerName, model.Head.CustomerNameSnapshot);
        Assert.IsNull(model.Head.ActiveConfirmationId);
        Assert.IsNull(model.Head.PackagingRequirements);
        Assert.AreEqual("软管 A", model.Head.InletrHoseType);
        Assert.AreEqual("起泡器 B", model.Head.AeratorType);
        Assert.AreEqual("业务员甲", model.Head.SoPerson);
        Assert.AreEqual("采购员乙", model.Head.PoPerson);
        Assert.AreEqual("说明书 C", model.Head.IMType);
        Assert.AreEqual("采购备注", model.Head.Remark);
        Assert.AreEqual(1, model.Lines.Count);
        Assert.AreEqual(2, model.Links.Count);
        Assert.AreEqual(model.Lines[0].Uid, model.Links[0].TargetLineId);
        Assert.AreEqual(model.Lines[0].Uid, model.Links[1].TargetLineId);
        Assert.AreEqual(model.Lines[0].Quantity, model.Links.Sum(x => x.AllocatedQty));
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

    /// <summary>明确零价保留，折扣记录不参与两种采购金额的计算。</summary>
    [TestMethod]
    public void Build_ExplicitZeroPricesPreserveAmountsWithoutDiscountCalculation()
    {
        var document = CreateDocument();
        document.Lines[0].UnitPrice = 0m;
        document.Lines[0].TaxPrice = 11m;
        document.Header.DiscountRate = 0.8m;
        document.Header.DiscountAmount = null;

        var model = _builder.Build(
            document, Guid.NewGuid(), Guid.NewGuid(), "PO2026070005", 1, createHead: true);

        Assert.AreEqual(0m, model.Lines.Single().UnitPrice);
        Assert.AreEqual(0m, model.Lines.Single().UnitAmount);
        Assert.AreEqual(11m, model.Lines.Single().TaxPrice);
        Assert.AreEqual(55m, model.Lines.Single().TaxAmount);
        Assert.AreEqual(0.8m, model.Head!.DiscountRate);
        Assert.IsNull(model.Head.DiscountAmount);
    }

    /// <summary>需求采购写模型拒绝缺失或超四位的价格，不能将缺价自动转成零价。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Build_RejectsMissingOrExcessPrecisionPrices(bool unitPrice)
    {
        foreach (var price in new decimal?[] { null, 1.000049m })
        {
            var document = CreateDocument();
            if (unitPrice) document.Lines[0].UnitPrice = price;
            else document.Lines[0].TaxPrice = price;
            Assert.ThrowsExactly<ProcurementOperationException>(() =>
                _builder.Build(document, Guid.NewGuid(), Guid.NewGuid(), "PO-INVALID-PRICE", 1, createHead: true));
        }
    }

    /// <summary>需求创建也使用四位远离零舍入，写入金额与后续确认规则一致。</summary>
    [TestMethod]
    public void Build_RoundsAmountsBeforeWriting()
    {
        var document = CreateDocument();
        document.Lines[0].UnitPrice = 0.0001m;
        document.Lines[0].TaxPrice = 0.0003m;
        document.Lines[0].PurchaseQty = 0.5m;
        document.Lines[0].Contributions.RemoveRange(1, document.Lines[0].Contributions.Count - 1);
        document.Lines[0].Contributions[0].PurchaseQty = 0.5m;
        var model = _builder.Build(document, Guid.NewGuid(), Guid.NewGuid(), "PO-ROUNDING", 1, createHead: true);
        Assert.AreEqual(0.0001m, model.Lines.Single().UnitAmount);
        Assert.AreEqual(0.0002m, model.Lines.Single().TaxAmount);
        ProcurementOrderPolicy.RequireConfirmationAmounts(model.Lines.Single());
    }

    private static PurchaseDraftPreviewDocumentDto CreateDocument()
    {
        var sourceUid = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var first = new PurchaseDraftSourceContributionDto
        {
            CommandItemId = Guid.NewGuid(),
            NetDemandId = Guid.NewGuid(),
            NetDemandVersion = 1,
            SupplierId = supplierId,
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
            NetDemandId = Guid.NewGuid(),
            NetDemandVersion = 1,
            SupplierId = supplierId,
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
            CustomerId = Guid.NewGuid(),
            CustomerCode = "CUS001",
            CustomerName = "客户一",
            SupplierId = supplierId,
            SupplierCode = "SUP001",
            SupplierName = "供应商一",
            Currency = CurrencyEnum.CNY,
            Header = new PurchaseDraftPreviewHeaderDto
            {
                Currency = CurrencyEnum.CNY,
                ExpectedDate = new DateTime(2026, 8, 10)
            },
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
