using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证反向资格必须证明完整下游历史，不能以净数量或单据状态代替流水证据。</summary>
[TestClass]
public sealed class ProcurementReversalPolicyTests
{
    /// <summary>相同入库事实同时阻断取消及作废；完整反向才放行，删行仅检查被删行。</summary>
    [TestMethod]
    public void InboundDraftPostedAndIncompleteReversalBlockWholeOperations()
    {
        var po = new PurchaseOrderHead { Uid = Guid.NewGuid(), Version = 1, ConfirmationStatus = ConfirmationStatusEnum.已确认 };
        var line = new PurchaseOrderLine { Uid = Guid.NewGuid(), Version = 1, Quantity = 3, UnlinkedQty = 3 };
        var inbound = new InboundDocument { Uid = Guid.NewGuid(), DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = Guid.NewGuid() };
        var row = new InboundDocumentLine { Uid = Guid.NewGuid(), InboundDocumentId = inbound.Uid, SourceLineId = line.Uid, Quantity = 3, ProductId = Guid.NewGuid(), UnitSnapshot = "PCS" };
        var facts = new ProcurementReversalFacts { Head = po, Lines = [line], Inbounds = [inbound], InboundLines = [row] };
        var request = new ProcurementReversalPreviewRequest { ExpectedVersion = 1 };
        foreach (var status in new[] { InboundPostingStatusEnum.Unposted, InboundPostingStatusEnum.Posted, InboundPostingStatusEnum.Reversed })
        {
            inbound.PostingStatus = status;
            Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.CancelConfirmation, request).CanExecute);
            Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.Void, request).CanExecute);
        }
        inbound.ReversedDate = DateTime.Now; inbound.ReverseReason = "完整反过账";
        var original = new InventoryLedgerEntry { Uid = Guid.NewGuid(), SourceDocumentType = "InboundDocument", SourceDocumentId = inbound.Uid,
            SourceLineId = row.Uid, CommandItemId = row.Uid, ProductId = row.ProductId, WarehouseSiteId = inbound.WarehouseSiteId, UnitSnapshot = "PCS",
            QuantityDelta = 3, TransactionType = InventoryTransactionTypeEnum.PurchaseReceiptInbound };
        var reverse = new InventoryLedgerEntry { Uid = Guid.NewGuid(), SourceDocumentType = "InboundDocument", SourceDocumentId = inbound.Uid,
            SourceLineId = row.Uid, ProductId = row.ProductId, WarehouseSiteId = inbound.WarehouseSiteId, UnitSnapshot = "PCS", QuantityDelta = -3,
            ReversalOfLedgerId = original.Uid, TransactionType = (InventoryTransactionTypeEnum)900 };
        facts.Ledgers = [original, reverse];
        Assert.IsTrue(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.CancelConfirmation, request).CanExecute);
        reverse.QuantityDelta = -2;
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.CancelConfirmation, request).CanExecute);
        reverse.QuantityDelta = -3;
        reverse.SourceDocumentId = Guid.NewGuid();
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.Void, request).CanExecute);
    }

    /// <summary>不相关行的入库不阻断指定删行，但行版本错误及全删仍必须拒绝。</summary>
    [TestMethod]
    public void DeleteChecksOnlySelectedRowsAndRejectsStaleOrAllRows()
    {
        var first = new PurchaseOrderLine { Uid = Guid.NewGuid(), Version = 2, Quantity = 1, UnlinkedQty = 1 };
        var other = new PurchaseOrderLine { Uid = Guid.NewGuid(), Version = 1, Quantity = 2, UnlinkedQty = 2 };
        var inbound = new InboundDocument { Uid = Guid.NewGuid(), PostingStatus = InboundPostingStatusEnum.Posted };
        var facts = new ProcurementReversalFacts { Head = new() { Uid = Guid.NewGuid(), Version = 3 }, Lines = [first, other],
            Inbounds = [inbound], InboundLines = [new() { InboundDocumentId = inbound.Uid, SourceLineId = other.Uid }] };
        var request = new ProcurementReversalPreviewRequest { ExpectedVersion = 3, Lines = [new() { PurchaseOrderLineId = first.Uid, Version = 2 }] };
        Assert.IsTrue(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.DeleteLines, request).CanExecute);
        request.Lines[0].Version = 1;
        Assert.ThrowsExactly<ProcurementConcurrencyException>(() => ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.DeleteLines, request));
        request.Lines[0].Version = 2;
        request.Lines.Add(new() { PurchaseOrderLineId = other.Uid, Version = 1 });
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.DeleteLines, request).CanExecute);
    }
}
