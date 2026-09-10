using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class PurchaseDraftPreviewBuilderTests
{
    private readonly PurchaseDraftPreviewBuilder _builder = new();

    [TestMethod]
    public void Build_ManualAllowsPartialQty_OneClickForcesAllRemainingQty()
    {
        var candidate = CreateCandidate(remainingQty: 10m);
        candidate.RequestedPurchaseQty = 4m;

        var manual = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate));
        var oneClick = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.SourceOneClick, candidate));

        Assert.AreEqual(4m, manual.SourceGroups.Single().Documents.Single().Lines.Single().PurchaseQty);
        Assert.AreEqual(10m, oneClick.SourceGroups.Single().Documents.Single().Lines.Single().PurchaseQty);
        Assert.IsFalse(manual.HasBlockers);
        Assert.IsFalse(oneClick.HasBlockers);
    }

    [TestMethod]
    public void Build_DifferentSourcesNeverSharePurchaseDocument()
    {
        var first = CreateCandidate(sourceNo: "SO001");
        var second = CreateCandidate(sourceNo: "SO002");
        second.SourceUid = Guid.NewGuid();
        second.SourceLineId = Guid.NewGuid();
        second.NetDemandId = Guid.NewGuid();
        second.CommandItemId = Guid.NewGuid();

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, first, second));

        Assert.AreEqual(2, result.SourceGroups.Count);
        Assert.AreEqual(2, result.SourceGroups.Sum(x => x.Documents.Count));
        Assert.IsTrue(result.SourceGroups.All(x => x.Documents.All(y => y.SourceUid == x.SourceUid)));
    }

    [TestMethod]
    public void Build_SameSourceDifferentSuppliersProducesDifferentPurchaseDocuments()
    {
        var first = CreateCandidate();
        var second = CloneForNextSourceLine(first);
        second.PurchaseSupplierId = Guid.NewGuid();
        second.PurchaseSupplierCode = "SUP002";

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, first, second));

        Assert.AreEqual(1, result.SourceGroups.Count);
        Assert.AreEqual(2, result.SourceGroups.Single().Documents.Count);
    }

    [TestMethod]
    public void Build_DefaultKeepSourceLinesPreservesSameSkuContributions()
    {
        var first = CreateCandidate();
        var second = CloneForNextSourceLine(first);

        var request = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, first, second);
        Assert.AreEqual(PurchaseDraftLineModeEnum.KeepSourceLines, request.LineMode);

        var result = _builder.Build(request);
        var document = result.SourceGroups.Single().Documents.Single();

        Assert.AreEqual(2, document.SourceLineCount);
        Assert.AreEqual(2, document.PreviewLineCount);
        Assert.IsTrue(document.Lines.All(x => x.Contributions.Count == 1));
    }

    [TestMethod]
    public void Build_MergeCompatibleLinesKeepsContributionsAndWarnsForDifferentDates()
    {
        var first = CreateCandidate(remainingQty: 3m);
        first.PlannedDeliveryDate = new DateTime(2026, 8, 12);
        var second = CloneForNextSourceLine(first);
        second.RemainingQty = 2m;
        second.RequestedPurchaseQty = 2m;
        second.PlannedDeliveryDate = new DateTime(2026, 8, 10);

        var request = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, first, second);
        request.LineMode = PurchaseDraftLineModeEnum.MergeCompatibleLines;

        var result = _builder.Build(request);
        var line = result.SourceGroups.Single().Documents.Single().Lines.Single();

        Assert.AreEqual(5m, line.PurchaseQty);
        Assert.AreEqual(2, line.Contributions.Count);
        Assert.AreEqual(new DateTime(2026, 8, 10), line.PlannedDeliveryDate);
        Assert.IsTrue(result.RequiresConfirmation);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates));
    }

    [TestMethod]
    public void Build_ManualParentOnlyWarnsWhenRelatedCustomerSuppliedDemandRemains()
    {
        var parent = CreateCandidate();
        var customerSupplied = CreateCandidate();
        customerSupplied.NetDemandId = Guid.NewGuid();
        customerSupplied.SourceLineId = Guid.NewGuid();
        customerSupplied.SourceLineNumber = 2;
        customerSupplied.CommandItemId = Guid.NewGuid();
        customerSupplied.IsSelected = false;
        customerSupplied.IsCustomerSupplied = true;
        customerSupplied.DeliveryTargetNetDemandId = parent.NetDemandId;
        customerSupplied.DeliveryTargetSupplierId = parent.PurchaseSupplierId;

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, parent, customerSupplied));

        var warning = result.Warnings.Single(x => x.Code == PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining);
        Assert.IsTrue(warning.RequiresConfirmation);
        Assert.IsFalse(warning.IsBlocker);
        CollectionAssert.Contains(warning.NetDemandIds, parent.NetDemandId);
        CollectionAssert.Contains(warning.NetDemandIds, customerSupplied.NetDemandId);
    }

    [TestMethod]
    public void Build_MissingSupplierProductOrPriceIsWarningButDoesNotBlock()
    {
        var candidate = CreateCandidate();
        candidate.HasSupplierProduct = false;
        candidate.UnitPrice = null;
        candidate.TaxPrice = null;

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate));

        Assert.IsFalse(result.HasBlockers);
        Assert.IsTrue(result.SourceGroups.Single().CanCreate);
        Assert.IsTrue(result.SourceGroups.Single().Documents.Single().Lines.Single().PurchaseDataIncomplete);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == PurchaseDraftWarningCodes.PurchaseDataIncomplete));
    }

    [TestMethod]
    public void Build_MissingSupplierBlocksWholeOneClickSource()
    {
        var valid = CreateCandidate();
        var invalid = CloneForNextSourceLine(valid);
        invalid.PurchaseSupplierId = null;
        invalid.PurchaseSupplierCode = string.Empty;
        invalid.PurchaseSupplierName = string.Empty;

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.SourceOneClick, valid, invalid));

        Assert.IsTrue(result.HasBlockers);
        Assert.IsFalse(result.SourceGroups.Single().CanCreate);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == PurchaseDraftWarningCodes.PurchaseSupplierRequired && x.IsBlocker));
    }

    [TestMethod]
    public void Build_CustomerSuppliedMissingDeliveryTargetIsBlocker()
    {
        var candidate = CreateCandidate();
        candidate.IsCustomerSupplied = true;
        candidate.DeliveryTargetNetDemandId = null;
        candidate.DeliveryTargetSupplierId = null;

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate));

        Assert.IsTrue(result.HasBlockers);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == PurchaseDraftWarningCodes.DeliveryTargetRequired));
        Assert.AreEqual(0, result.SourceGroups.Single().Documents.Count);
    }

    [TestMethod]
    public void Build_AlwaysUsesCnyAndPreviewTokenChangesWithLineMode()
    {
        var candidate = CreateCandidate();
        var keepRequest = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate);
        var mergeRequest = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate);
        mergeRequest.ExecutionRequestId = keepRequest.ExecutionRequestId;
        mergeRequest.LineMode = PurchaseDraftLineModeEnum.MergeCompatibleLines;

        var keep = _builder.Build(keepRequest);
        var merge = _builder.Build(mergeRequest);

        Assert.AreEqual("CNY", keep.Currency);
        Assert.AreEqual("CNY", keep.SourceGroups.Single().Documents.Single().Currency);
        Assert.AreNotEqual(keep.PreviewToken, merge.PreviewToken);
        Assert.AreEqual(64, keep.PreviewToken.Length);
    }

    [TestMethod]
    public void Build_ProductUnitMustFlowIntoPurchaseLineSnapshot()
    {
        var candidate = CreateCandidate();
        candidate.Unit = "BOX";

        var result = _builder.Build(
            CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate));

        Assert.AreEqual(
            "BOX",
            result.SourceGroups.Single().Documents.Single().Lines.Single().Unit);
    }

    [TestMethod]
    public void Build_OneClickSkipsProductionAndOutsourcingPlans()
    {
        var purchase = CreateCandidate();
        var production = CloneForNextSourceLine(purchase);
        production.ExecutionType = DemandExecutionTypeEnum.Production;
        var outsourcing = CloneForNextSourceLine(purchase);
        outsourcing.NetDemandId = Guid.NewGuid();
        outsourcing.SourceLineId = Guid.NewGuid();
        outsourcing.SourceLineNumber = 3;
        outsourcing.CommandItemId = Guid.NewGuid();
        outsourcing.ExecutionType = DemandExecutionTypeEnum.Outsourcing;

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.SourceOneClick, purchase, production, outsourcing));

        Assert.AreEqual(2, result.SkippedItems.Count);
        Assert.AreEqual(1, result.SourceGroups.Single().Documents.Single().Lines.Count);
        Assert.IsFalse(result.HasBlockers);
    }

    [TestMethod]
    public void Build_MultipleSourcesReturnIndependentSourceTokens()
    {
        var first = CreateCandidate(sourceNo: "SO001");
        var second = CreateCandidate(sourceNo: "SO002");

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, first, second));

        Assert.AreEqual(2, result.SourceGroups.Count);
        Assert.IsTrue(result.SourceGroups.All(x => x.SourcePreviewToken.Length == 64));
        Assert.AreNotEqual(result.SourceGroups[0].SourcePreviewToken, result.SourceGroups[1].SourcePreviewToken);
    }

    [TestMethod]
    public void Build_OneClickRejectsCandidatesFromAnotherSource()
    {
        var first = CreateCandidate(sourceNo: "SO001");
        var second = CreateCandidate(sourceNo: "SO002");
        var request = CreateRequest(PurchaseDraftEntryModeEnum.SourceOneClick, first, second);

        Assert.ThrowsExactly<ArgumentException>(() => _builder.Build(request));
    }

    [TestMethod]
    public void Build_SameSourceUidWithDifferentSourceNumbersIsBlockedAsCorrupted()
    {
        var first = CreateCandidate(sourceNo: "SO001");
        var second = CloneForNextSourceLine(first);
        second.SourceNo = "SO-WRONG";

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, first, second));

        Assert.IsTrue(result.HasBlockers);
        Assert.AreEqual(1, result.SourceGroups.Count);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == PurchaseDraftWarningCodes.CorruptedPlanningState));
    }

    [TestMethod]
    public void Build_PreviewTokenIncludesSupplierProductSnapshot()
    {
        var candidate = CreateCandidate();
        var request = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate);
        var before = _builder.Build(request);

        candidate.SupplierSku = "S-SKU-CHANGED";
        var after = _builder.Build(request);

        Assert.AreNotEqual(before.PreviewToken, after.PreviewToken);
    }

    [TestMethod]
    public void Build_InactiveSupplierIsBlocker()
    {
        var candidate = CreateCandidate();
        candidate.PurchaseSupplierIsActive = false;

        var result = _builder.Build(CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate));

        Assert.IsTrue(result.HasBlockers);
        Assert.IsTrue(result.Warnings.Any(x => x.Code == PurchaseDraftWarningCodes.PurchaseSupplierInvalid));
    }

    [TestMethod]
    public void Build_ImplicitAndExplicitCreateNewProduceSamePreviewTokens()
    {
        var candidate = CreateCandidate();
        var initialRequest = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate);
        var createRequest = CreateRequest(PurchaseDraftEntryModeEnum.ManualSelection, candidate);
        createRequest.ExecutionRequestId = initialRequest.ExecutionRequestId;
        createRequest.Targets = new List<PurchaseDraftPreviewBuildTarget>
        {
            new()
            {
                SourceType = candidate.SourceType,
                SourceUid = candidate.SourceUid,
                SupplierId = candidate.PurchaseSupplierId!.Value,
                TargetMode = PurchaseDraftTargetModeEnum.CreateNew
            }
        };

        var initial = _builder.Build(initialRequest);
        var create = _builder.Build(createRequest);

        Assert.AreEqual(initial.PreviewToken, create.PreviewToken);
        Assert.AreEqual(
            initial.SourceGroups.Single().SourcePreviewToken,
            create.SourceGroups.Single().SourcePreviewToken);
    }

    private static PurchaseDraftPreviewBuildRequest CreateRequest(
        PurchaseDraftEntryModeEnum entryMode,
        params PurchaseDraftPreviewCandidate[] candidates)
    {
        var first = candidates.FirstOrDefault();
        return new PurchaseDraftPreviewBuildRequest
        {
            ExecutionRequestId = Guid.NewGuid(),
            EntryMode = entryMode,
            LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
            SourceType = entryMode == PurchaseDraftEntryModeEnum.SourceOneClick ? first?.SourceType ?? string.Empty : string.Empty,
            SourceUid = entryMode == PurchaseDraftEntryModeEnum.SourceOneClick ? first?.SourceUid : null,
            Candidates = candidates
        };
    }

    private static PurchaseDraftPreviewCandidate CreateCandidate(
        decimal remainingQty = 5m,
        string sourceNo = "SO001")
    {
        var sourceUid = Guid.NewGuid();
        return new PurchaseDraftPreviewCandidate
        {
            CommandItemId = Guid.NewGuid(),
            NetDemandId = Guid.NewGuid(),
            NetDemandVersion = 1,
            SourceType = "SO",
            SourceUid = sourceUid,
            SourceNo = sourceNo,
            SourceLineId = Guid.NewGuid(),
            SourceLineNumber = 1,
            IsSelected = true,
            ExecutionType = DemandExecutionTypeEnum.Purchase,
            RemainingQty = remainingQty,
            RequestedPurchaseQty = remainingQty,
            DemandRequiredDate = new DateTime(2026, 8, 12),
            PlannedDeliveryDate = new DateTime(2026, 8, 10),
            ProductId = Guid.NewGuid(),
            Sku = "SKU001",
            LinkSku = "LINK001",
            CustomerSku = "CUS001",
            Description = "测试产品",
            MaterialType = "成品",
            Unit = "PCS",
            QuantityScale = 4,
            PurchaseSupplierId = Guid.NewGuid(),
            PurchaseSupplierCode = "SUP001",
            PurchaseSupplierName = "供应商一",
            HasSupplierProduct = true,
            SupplierSku = "S-SKU001",
            UnitPrice = 12.5m,
            TaxPrice = 14.125m
        };
    }

    private static PurchaseDraftPreviewCandidate CloneForNextSourceLine(PurchaseDraftPreviewCandidate source)
        => new()
        {
            CommandItemId = Guid.NewGuid(),
            NetDemandId = Guid.NewGuid(),
            NetDemandVersion = source.NetDemandVersion,
            SourceType = source.SourceType,
            SourceUid = source.SourceUid,
            SourceNo = source.SourceNo,
            SourceLineId = Guid.NewGuid(),
            SourceLineNumber = (source.SourceLineNumber ?? 0) + 1,
            IsSelected = source.IsSelected,
            IsCustomerSupplied = source.IsCustomerSupplied,
            ExecutionType = source.ExecutionType,
            RemainingQty = source.RemainingQty,
            RequestedPurchaseQty = source.RequestedPurchaseQty,
            DemandRequiredDate = source.DemandRequiredDate,
            PlannedDeliveryDate = source.PlannedDeliveryDate,
            ProductId = source.ProductId,
            Sku = source.Sku,
            LinkSku = source.LinkSku,
            CustomerSku = source.CustomerSku,
            Description = source.Description,
            MaterialType = source.MaterialType,
            Unit = source.Unit,
            QuantityScale = source.QuantityScale,
            PurchaseSupplierId = source.PurchaseSupplierId,
            PurchaseSupplierCode = source.PurchaseSupplierCode,
            PurchaseSupplierName = source.PurchaseSupplierName,
            DeliveryTargetNetDemandId = source.DeliveryTargetNetDemandId,
            DeliveryTargetSupplierId = source.DeliveryTargetSupplierId,
            DeliveryTargetSupplierCode = source.DeliveryTargetSupplierCode,
            DeliveryTargetSupplierName = source.DeliveryTargetSupplierName,
            HasSupplierProduct = source.HasSupplierProduct,
            SupplierSku = source.SupplierSku,
            UnitPrice = source.UnitPrice,
            TaxPrice = source.TaxPrice
        };
}
