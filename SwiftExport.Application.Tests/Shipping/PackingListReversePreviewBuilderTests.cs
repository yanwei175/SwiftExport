using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证整单反向只使用原事实及严格余额保护，不冒充数据库原子性验证。</summary>
[TestClass]
public sealed class PackingListReversePreviewBuilderTests
{
    /// <summary>余额为零时先恢复出库再撤销自动收货，可正常反向。</summary>
    [TestMethod]
    public void RestoresOutboundBeforeReversingReceipt()
    {
        var f = new Fixture();
        var preview = f.Build();
        Assert.AreEqual(0, preview.Blockers.Count);
        Assert.IsNotNull(preview.Token);
        Assert.AreEqual("Shipment", preview.Actions[0].Kind);
        Assert.AreEqual("Receipt", preview.Actions[1].Kind);
    }

    /// <summary>原流水不能缺失，也不能隐藏已经存在的反向流水。</summary>
    [TestMethod]
    public void MissingOrAlreadyReversedLedgerBlocks()
    {
        var f = new Fixture();
        var original = f.Facts.Ledgers;
        f.Facts.Ledgers = [original[0]];
        Assert.AreEqual("LEDGER_INTEGRITY", f.Build().Blockers.Single().Code);
        f.Facts.Ledgers = [.. original, new InventoryLedgerEntry { ReversalOfLedgerId = original[0].Uid }];
        Assert.AreEqual("LEDGER_INTEGRITY", f.Build().Blockers.Single().Code);
    }

    /// <summary>不能反向其他箱单的自动单据，即使原证据指向该单据。</summary>
    [TestMethod]
    public void CrossPackingListDocumentBlocks()
    {
        var f = new Fixture();
        f.Facts.InboundHeads[0].TriggerPackingListId = Guid.NewGuid();
        Assert.AreEqual("DOCUMENT_INTEGRITY", f.Build().Blockers.Single().Code);
    }

    /// <summary>单据版本变化会阻断；余额变化只要仍合法也会使令牌变化。</summary>
    [TestMethod]
    public void VersionsAndBalancesAreProtected()
    {
        var f = new Fixture();
        var token = f.Build().Token;
        f.Facts.Balances[0].OnHandQty = 1;
        f.Facts.Balances[0].Version++;
        Assert.AreNotEqual(token, f.Build().Token);
        f.Facts.OutboundHeads[0].Version++;
        Assert.AreEqual("DOCUMENT_INTEGRITY", f.Build().Blockers.Single().Code);
    }

    /// <summary>恢复出库后仍不足以撤销自动入库时，普通反向必须阻断。</summary>
    [TestMethod]
    public void NegativeBalanceBlocks()
    {
        var f = new Fixture();
        f.Facts.PackingLines[0].Qty = 5;
        f.Facts.OutboundLines[0].Quantity = 5;
        f.Facts.Evidence[1].Qty = 5;
        f.Facts.Ledgers[1].QuantityDelta = -5;
        Assert.AreEqual("NEGATIVE_STOCK", f.Build().Blockers.Single().Code);
        Assert.AreEqual(0, f.Build().Actions.Count);
    }

    /// <summary>无库存交付也要求原执行证据完整，但不需要仓库和流水。</summary>
    [TestMethod]
    public void DeliveryOnlyReverseNeedsNoStock()
    {
        var f = new Fixture();
        var evidence = f.Facts.Evidence[1];
        evidence.ActionKind = "Delivery";
        evidence.DocumentId = evidence.DocumentLineId = evidence.LedgerId = evidence.WarehouseSiteId = null;
        f.Facts.Evidence = [evidence];
        f.Facts.InboundHeads = []; f.Facts.InboundLines = []; f.Facts.OutboundHeads = []; f.Facts.OutboundLines = [];
        f.Facts.Ledgers = []; f.Facts.Balances = []; f.Facts.Warehouses = [];
        Assert.AreEqual(0, f.Build().Blockers.Count);
        f.Facts.Head!.PostingStatus = PackingListPostingStatusEnum.Reversed;
        Assert.AreEqual("INVALID_STATE", f.Build().Blockers.Single().Code);
    }

    /// <summary>反向证据引用原执行行并保存新增反向流水，原对象不被覆盖。</summary>
    [TestMethod]
    public void ReverseEvidencePreservesOriginalAndUsesNewLedger()
    {
        var f = new Fixture();
        var receipt = Return(f.Facts.Evidence[0]);
        var shipment = Return(f.Facts.Evidence[1]);
        var originalLedger = f.Facts.Evidence[0].LedgerId;
        var result = SwiftExport.Application.Services.Shipping.PackingLists.PackingListReversalService.CreateReverseEvidence(Guid.NewGuid(), f.Facts,
            new() { Documents = [receipt] }, new() { Documents = [shipment] });
        Assert.AreEqual(originalLedger, f.Facts.Evidence[0].LedgerId);
        Assert.AreEqual(f.Facts.Evidence[0].Uid, result[0].OriginalExecutionLineId);
        Assert.AreEqual(receipt.Lines[0].LedgerId, result[0].LedgerId);

        static AutomaticDocumentResult Return(PackingListPostingExecutionLine line) => new()
        {
            DocumentId = line.DocumentId!.Value, DocumentNo = "REVERSED", Version = 3, PostingStatus = 8,
            Lines = [new(line.DocumentLineId!.Value, line.DocumentLineId.Value, Guid.NewGuid())]
        };
    }

    /// <summary>拒绝版本未推进、请求身份错配、重复流水和冒用其他原流水的端口回执。</summary>
    [TestMethod]
    [DataRow("version")]
    [DataRow("identity")]
    [DataRow("duplicate")]
    [DataRow("original")]
    public void ReverseEvidenceRejectsInvalidPortResult(string defect)
    {
        var f = new Fixture();
        var first = f.Facts.Evidence[0];
        var second = f.Facts.Evidence[1];
        var ledger = Guid.NewGuid();
        var receipt = new AutomaticDocumentResult
        {
            DocumentId = first.DocumentId!.Value, Version = defect == "version" ? 2 : 3, PostingStatus = 8,
            Lines = [new(defect == "identity" ? Guid.NewGuid() : first.DocumentLineId!.Value,
                first.DocumentLineId!.Value, ledger)]
        };
        var shipment = new AutomaticDocumentResult
        {
            DocumentId = second.DocumentId!.Value, Version = 3, PostingStatus = 8,
            Lines = [new(second.DocumentLineId!.Value, second.DocumentLineId.Value,
                defect == "duplicate" ? ledger : defect == "original" ? first.LedgerId!.Value : Guid.NewGuid())]
        };
        var error = Assert.Throws<SwiftExport.Application.Interfaces.Shipping.PackingListPostingFailure>(() =>
            SwiftExport.Application.Services.Shipping.PackingLists.PackingListReversalService.CreateReverseEvidence(Guid.NewGuid(), f.Facts,
                new() { Documents = [receipt] }, new() { Documents = [shipment] }));
        Assert.AreEqual("EXECUTION_INVALID", error.Code);
    }

    /// <summary>构造完整自动收货及出库事实，模拟生成后余额为零。</summary>
    internal sealed class Fixture
    {
        public PackingListReverseFacts Facts { get; } = new();
        public Fixture()
        {
            var command = Guid.NewGuid();
            var head = new PackingListHead { PostCommandId = command, PostingStatus = PackingListPostingStatusEnum.Posted };
            var product = Guid.NewGuid();
            var warehouse = new WarehouseSite { IsActive = true };
            var packingLine = new PackingListLine { PackingListHeadId = head.Uid, GrossDemandId = Guid.NewGuid(), ProductId = product, UnitSnapshot = "PCS", Qty = 10 };
            var execution = new PackingListPostingExecution { PackingListHeadId = head.Uid, CommandId = command, Operation = 1 };
            var ih = new InboundDocument { TriggerPackingListId = head.Uid, WarehouseSiteId = warehouse.Uid, Version = 2,
                CreationMode = InboundCreationModeEnum.Automatic, DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, PostingStatus = InboundPostingStatusEnum.Posted };
            var oh = new OutboundDocument { TriggerPackingListId = head.Uid, WarehouseSiteId = warehouse.Uid, Version = 2,
                DocumentType = OutboundDocumentTypeEnum.FinalCustomerShipment, PostingStatus = OutboundPostingStatusEnum.Posted };
            var il = new InboundDocumentLine { InboundDocumentId = ih.Uid, ProductId = product, Quantity = 10, UnitSnapshot = "PCS", SourceLineId = Guid.NewGuid() };
            var ol = new OutboundDocumentLine { OutboundDocumentId = oh.Uid, ProductId = product, Quantity = 10, UnitSnapshot = "PCS", CommandItemId = Guid.NewGuid() };
            var incoming = new InventoryLedgerEntry { SourceDocumentId = ih.Uid, SourceLineId = il.Uid, SourceDocumentType = "InboundDocument",
                CommandItemId = il.Uid, WarehouseSiteId = warehouse.Uid, ProductId = product, QuantityDelta = 10,
                TransactionType = InventoryTransactionTypeEnum.PackingListAutoReceiptInbound, UnitSnapshot = "PCS" };
            var outgoing = new InventoryLedgerEntry { SourceDocumentId = oh.Uid, SourceLineId = ol.Uid, SourceDocumentType = "OutboundDocument",
                CommandItemId = ol.CommandItemId, WarehouseSiteId = warehouse.Uid, ProductId = product, QuantityDelta = -10,
                TransactionType = InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound, UnitSnapshot = "PCS" };
            Facts.Head = head; Facts.PackingLines = [packingLine]; Facts.Executions = [execution];
            Facts.InboundHeads = [ih]; Facts.InboundLines = [il]; Facts.OutboundHeads = [oh]; Facts.OutboundLines = [ol];
            Facts.Ledgers = [incoming, outgoing]; Facts.Warehouses = [warehouse];
            Facts.Balances = [new InventoryBalance { WarehouseSiteId = warehouse.Uid, ProductId = product, OnHandQty = 0, UnitSnapshot = "PCS" }];
            Facts.Evidence = [Evidence(1, "Receipt", incoming, il.SourceLineId), Evidence(2, "Shipment", outgoing, null)];
            PackingListPostingExecutionLine Evidence(int number, string kind, InventoryLedgerEntry ledger, Guid? purchase) => new()
            {
                ExecutionId = execution.Uid, LineNumber = number, PackingListLineId = packingLine.Uid, GrossDemandId = packingLine.GrossDemandId,
                ProductId = product, Unit = "PCS", WarehouseSiteId = warehouse.Uid, ActionKind = kind, Qty = 10, PurchaseLineId = purchase,
                DocumentId = ledger.SourceDocumentId, DocumentLineId = ledger.SourceLineId, LedgerId = ledger.Uid, DocumentVersion = 2
            };
        }
        public PackingListPostingPreviewBuilder.Preview Build() => new PackingListReversePreviewBuilder().Build(Facts, new DateTime(2026, 9, 17), "录单有误");
    }
}
