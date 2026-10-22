using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Contracts.DTOs.Errors;
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
    /// <summary>同单另一采购行关联的异常入库头，不应阻止删除无关的有效草稿行。</summary>
    [TestMethod]
    public void DeleteLinesIgnoresAnomalyOnAnotherInboundHead()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();var badHead=Guid.NewGuid();var otherHead=Guid.NewGuid();
        var facts=new ProcurementReversalFacts
        {
            Head=new(){Uid=Guid.NewGuid(),Version=1},
            Lines=[new(){Uid=first,Version=1,Quantity=1,UnlinkedQty=1},new(){Uid=second,Version=1,Quantity=1,UnlinkedQty=1}],
            Inbounds=[new(){Uid=badHead},new(){Uid=otherHead,IsDeleted=true}],
            InboundLines=[new(){InboundDocumentId=badHead,SourceLineId=first},new(){InboundDocumentId=badHead,SourceLineId=Guid.NewGuid()},
                new(){InboundDocumentId=otherHead,SourceLineId=second,IsDeleted=true}],
            UnattributedInboundHeadIds=[badHead]
        };
        var preview=ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteLines,
            new(){ExpectedVersion=1,Lines=[new(){PurchaseOrderLineId=second,Version=1}]});
        Assert.IsTrue(preview.CanExecute,string.Join(";",preview.Blockers));
    }

    /// <summary>选定行关联异常整头时仍阻断；同头孤立、旧类型、跨采购或已删关联不能被选行过滤掩盖。</summary>
    [TestMethod]
    [DataRow("orphan")]
    [DataRow("legacy11")]
    [DataRow("legacy12")]
    [DataRow("cross-order")]
    [DataRow("deleted-association")]
    public void DeleteLinesBlocksEverySelectedLineRelatedToAnomalousHead(string kind)
    {
        var selected=Guid.NewGuid();var other=Guid.NewGuid();var badHead=Guid.NewGuid();
        var facts=new ProcurementReversalFacts
        {
            Head=new(){Uid=Guid.NewGuid(),Version=1},
            Lines=[new(){Uid=selected,Version=1,Quantity=1,UnlinkedQty=1},new(){Uid=other,Version=1,Quantity=1,UnlinkedQty=1}],
            Inbounds=[new(){Uid=badHead,DocumentType=kind=="legacy11"?InboundDocumentTypeEnum.LegacyCustomerSuppliedReceipt
                :kind=="legacy12"?InboundDocumentTypeEnum.LegacyPackingListAutoReceipt:InboundDocumentTypeEnum.PurchaseReceipt}],
            InboundLines=[new(){InboundDocumentId=badHead,SourceLineId=selected,IsDeleted=kind=="deleted-association"},
                new(){InboundDocumentId=badHead,SourceLineId=kind is "cross-order" or "orphan"?Guid.NewGuid():other}],
            UnattributedInboundHeadIds=[badHead]
        };
        var preview=ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteLines,
            new(){ExpectedVersion=1,Lines=[new(){PurchaseOrderLineId=selected,Version=1}]});
        Assert.IsFalse(preview.CanExecute);
        Assert.IsTrue(preview.Blockers.Any(message=>message.Contains("来源无法归属",StringComparison.Ordinal)));
        if(kind=="cross-order")
        {
            // 同一异常头还关联另一订单的行；该订单读取同头完整证据后也必须拒绝相应删行。
            var foreign=facts.InboundLines[1].SourceLineId;
            facts.Head.Uid=Guid.NewGuid();
            facts.Lines=[new(){Uid=foreign,Version=1,Quantity=1,UnlinkedQty=1},new(){Uid=Guid.NewGuid(),Version=1,Quantity=1,UnlinkedQty=1}];
            var otherPreview=ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteLines,
                new(){ExpectedVersion=1,Lines=[new(){PurchaseOrderLineId=foreign,Version=1}]});
            Assert.IsFalse(otherPreview.CanExecute);
            Assert.IsTrue(otherPreview.Blockers.Any(message=>message.Contains("来源无法归属",StringComparison.Ordinal)));
        }
    }


    /// <summary>旧需求行空值只在全量活动贡献可证明时允许作废，其他命令及真正数量异常仍阻断。</summary>
    [TestMethod]
    public void LegacyUnknownUnlinkedQuantityAllowsOnlyProvenWholeVoid()
    {
        var head = new PurchaseOrderHead { Uid = Guid.NewGuid(), Version = 1, OriginKind = 1, ConfirmationStatus = ConfirmationStatusEnum.已确认 };
        var source = new SwiftExport.Core.Entities.DemandPlanning.GrossDemand { Uid = Guid.NewGuid() };
        var demand = new SwiftExport.Core.Entities.DemandPlanning.NetDemand { Uid = Guid.NewGuid(), GrossDemandId = source.Uid };
        var line = new PurchaseOrderLine { Uid = Guid.NewGuid(), Version = 1, Quantity = 5, UnlinkedQty = null };
        var link = new SwiftExport.Core.Entities.DemandPlanning.DemandExecutionLink
        {
            Uid = Guid.NewGuid(), NetDemandId = demand.Uid, TargetType = "PO", TargetHeadId = head.Uid,
            TargetLineId = line.Uid, AllocatedQty = 5, LinkStatus = DemandExecutionLinkStatusEnum.Active
        };
        var facts = new ProcurementReversalFacts { Head = head, Lines = [line], Sources = [source], Demands = [demand], Links = [link] };
        var request = new ProcurementReversalPreviewRequest { ExpectedVersion = 1 };
        var preview = ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.Void, request);
        Assert.IsTrue(preview.CanExecute);
        Assert.AreEqual(5m, preview.Contributions.Single().Quantity);
        Assert.IsNull(line.UnlinkedQty);
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.CancelConfirmation, request).CanExecute);
        foreach (var quantity in new[] { 4m, 6m })
        {
            link.AllocatedQty = quantity;
            Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.Void, request).CanExecute);
        }
        link.AllocatedQty = 5;
        demand.IsDeleted = true;
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.Void, request).CanExecute);
        demand.IsDeleted = false;
        facts.Links.Clear();
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.Void, request).CanExecute);
    }

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
        var conflict = Assert.ThrowsExactly<ProcurementOperationException>(() => ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.DeleteLines, request));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, conflict.Code);
        Assert.AreEqual("ExpectedVersion", conflict.Field);
        request.Lines[0].Version = 2;
        request.Lines.Add(new() { PurchaseOrderLineId = other.Uid, Version = 1 });
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts, ProcurementReversalOperation.DeleteLines, request).CanExecute);
    }
}
