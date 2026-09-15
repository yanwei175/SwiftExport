using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>三个采购反向操作共用的纯资格规则；历史入库须证明完整反向，不能以余额为零放行。</summary>
public static class ProcurementReversalPolicy
{
    /// <summary>依据事务内锁定事实生成预览；取消确认不释放贡献，删行只针对选定行。</summary>
    public static ProcurementReversalPreviewDto Build(ProcurementReversalFacts facts, ProcurementReversalOperation operation, ProcurementReversalPreviewRequest request)
    {
        var head = facts.Head ?? throw new InvalidOperationException("采购单不存在。");
        var preview = new ProcurementReversalPreviewDto { PurchaseOrderId = head.Uid, Version = head.Version, OrderNo = head.OrderNo, Operation = operation };
        if (request.ExpectedVersion <= 0 || head.Version != request.ExpectedVersion)
            throw new ProcurementConcurrencyException("采购版本已变化，请重新预览。");
        if (!Enum.IsDefined(operation)) throw new InvalidOperationException("未知采购反向操作。");
        if (head.IsDeleted || head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active)
            preview.Blockers.Add("仅活动采购允许此操作。");
        if (operation == ProcurementReversalOperation.CancelConfirmation && head.ConfirmationStatus != ConfirmationStatusEnum.已确认)
            preview.Blockers.Add("仅已确认采购可以取消确认。");
        if (operation == ProcurementReversalOperation.DeleteLines && head.ConfirmationStatus != ConfirmationStatusEnum.待确认)
            preview.Blockers.Add("仅待确认采购草稿可以删除行。");
        if (head.ConfirmationStatus is not (ConfirmationStatusEnum.待确认 or ConfirmationStatusEnum.已确认))
            preview.Blockers.Add("采购确认状态异常。");
        var active = facts.Lines.Where(x => !x.IsDeleted).ToList();
        var selected = active;
        if (operation == ProcurementReversalOperation.DeleteLines)
        {
            if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(x => x is null || x.PurchaseOrderLineId == Guid.Empty || x.Version <= 0)
                || request.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != request.Lines.Count)
                throw new InvalidOperationException("请选择有效且不重复的采购行及版本。");
            selected = active.Where(x => request.Lines.Any(y => y.PurchaseOrderLineId == x.Uid)).ToList();
            if (selected.Count != request.Lines.Count || selected.Any(x => request.Lines.Single(y => y.PurchaseOrderLineId == x.Uid).Version != x.Version))
                throw new ProcurementConcurrencyException("选定采购行不存在或版本已变化。");
            if (selected.Count >= active.Count) preview.Blockers.Add("不能删除全部剩余行，请使用整单作废。");
        }
        else if (request.Lines is null || request.Lines.Count != 0) throw new InvalidOperationException("整单操作不能指定部分行。");
        if (active.Count == 0) preview.Blockers.Add("采购单缺少有效明细。");
        if (facts.HasUnattributedInbound) preview.Blockers.Add("存在来源无法归属的采购入库，请先核实异常来源后再操作。");
        var ids = selected.Select(x => x.Uid).ToHashSet();
        preview.Lines = selected.OrderBy(x => x.LineNumber).Select(x => new ProcurementReversalLineDto
        { PurchaseOrderLineId = x.Uid, Version = x.Version, LineNumber = x.LineNumber, Sku = x.Sku, Quantity = x.Quantity }).ToList();
        var own = facts.Links.Where(x => x.TargetHeadId == head.Uid || facts.Lines.Any(l => l.Uid == x.TargetLineId)).ToList();
        if (own.Any(x => x.TargetType != "PO" || x.TargetHeadId != head.Uid || !facts.Lines.Any(l => l.Uid == x.TargetLineId)
            || !facts.Demands.Any(n => n.Uid == x.NetDemandId) || x.AllocatedQty <= 0 || x.ReleasedQty < 0 || x.ReleasedQty > x.AllocatedQty
            || (x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active)
            || (x.LinkStatus == DemandExecutionLinkStatusEnum.Active && facts.Lines.Any(l => l.Uid == x.TargetLineId && l.IsDeleted))))
            preview.Blockers.Add("采购与物料需求的历史引用不完整或数量异常，不能安全处理。");
        foreach (var line in selected)
        {
            var links = own.Where(x => x.TargetLineId == line.Uid && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active).ToList();
            if ((head.OriginKind == 1 || line.SourceUid.HasValue) && links.Count == 0)
                preview.Blockers.Add($"第 {line.LineNumber} 行缺少活动需求引用。");
            if (links.Count > 0 && links.Sum(x => x.AllocatedQty - x.ReleasedQty) + line.UnlinkedQty != line.Quantity)
                preview.Blockers.Add($"第 {line.LineNumber} 行采购数量与有效需求贡献不一致。");
            if (operation == ProcurementReversalOperation.CancelConfirmation) continue;
            foreach (var link in links)
            {
                var demand = facts.Demands.SingleOrDefault(x => x.Uid == link.NetDemandId);
                var source = facts.Sources.SingleOrDefault(x => x.Uid == demand?.GrossDemandId);
                if (demand is null || source is null || demand.IsDeleted || source.IsDeleted || demand.LifecycleStatus != NetDemandLifecycleStatusEnum.Active)
                { preview.Blockers.Add("活动采购引用对应的来源或物料需求已失效。"); continue; }
                preview.Contributions.Add(new ProcurementReleasedContributionDto { LinkId = link.Uid, PurchaseOrderLineId = line.Uid,
                    NetDemandId = demand.Uid, GrossDemandId = source.Uid, SourceNo = source.SourceNo, Sku = demand.SkuSnapshot,
                    Quantity = link.AllocatedQty - link.ReleasedQty });
            }
        }
        foreach (var report in facts.Reports.Where(x => ids.Contains(x.PurchaseOrderLineId)).GroupBy(x => x.PurchaseOrderLineId).Select(x => x.OrderByDescending(r => r.Sequence).First()))
        {
            var line = selected.Single(x => x.Uid == report.PurchaseOrderLineId);
            preview.Warnings.Add($"第 {line.LineNumber} 行 {line.Sku} 有供应商反馈：排产 {report.ArrangementStatus}，已备好 {report.ReadyQty}；原反馈将保留。");
        }
        var released = preview.Contributions.Select(x => x.LinkId).ToHashSet();
        foreach (var sourceId in preview.Contributions.Select(x => x.GrossDemandId).Distinct())
        {
            var netIds = facts.Demands.Where(x => x.GrossDemandId == sourceId && !x.IsDeleted && x.LifecycleStatus == NetDemandLifecycleStatusEnum.Active).Select(x => x.Uid).ToHashSet();
            var other = facts.Links.Where(x => netIds.Contains(x.NetDemandId) && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active && !released.Contains(x.Uid)).Select(x => x.TargetNo).Distinct().ToArray();
            if (other.Length > 0) preview.Warnings.Add($"来源 {facts.Sources.Single(x => x.Uid == sourceId).SourceNo} 仍有活动执行引用：{string.Join("、", other)}；本次完成后仍不能回退该来源的整组物料。");
        }
        var downstreamIds = operation == ProcurementReversalOperation.DeleteLines ? ids : facts.Lines.Select(x => x.Uid).ToHashSet();
        foreach (var inbound in facts.Inbounds.Where(h => facts.InboundLines.Any(l => l.InboundDocumentId == h.Uid && downstreamIds.Contains(l.SourceLineId))))
        {
            var rows = facts.InboundLines.Where(x => x.InboundDocumentId == inbound.Uid).ToList();
            var affectedActive = rows.Any(x => !x.IsDeleted && downstreamIds.Contains(x.SourceLineId));
            var ledgers = facts.Ledgers.Where(x => x.SourceDocumentId == inbound.Uid || rows.Any(l => l.Uid == x.SourceLineId)).ToList();
            if (inbound.PostingStatus == InboundPostingStatusEnum.Unposted && ledgers.Count == 0)
            {
                if (!inbound.IsDeleted && affectedActive && inbound.LifecycleStatus == InventoryDocumentLifecycleStatusEnum.Active)
                    preview.Blockers.Add($"入库草稿 {inbound.DocumentNo} 仍引用采购行，请先处理草稿。");
                continue;
            }
            if (inbound.PostingStatus == InboundPostingStatusEnum.Posted)
                preview.Blockers.Add($"入库单 {inbound.DocumentNo} 尚未反过账。");
            else if (!HasCompleteReversal(inbound, rows, facts.Ledgers))
                preview.Blockers.Add($"入库单 {inbound.DocumentNo} 的来源或反向流水不完整，不能放行。");
        }
        preview.Blockers = preview.Blockers.Distinct().ToList();
        preview.CanExecute = preview.Blockers.Count == 0;
        return preview;
    }

    /// <summary>逐原行证明一原一反向流水，拒绝额外替代、删除和跨凭证反向事实。</summary>
    public static bool HasCompleteReversal(InboundDocument head, IReadOnlyList<InboundDocumentLine> rows, IReadOnlyList<InventoryLedgerEntry> all)
    {
        if (head.IsDeleted || head.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt || head.PostingStatus != InboundPostingStatusEnum.Reversed
            || head.CreationMode != InboundCreationModeEnum.Manual || head.TriggerPackingListId.HasValue || !head.ReversedDate.HasValue
            || string.IsNullOrWhiteSpace(head.ReverseReason) || rows.Count == 0 || rows.Any(x => x.IsDeleted || x.SourceLineId == Guid.Empty || x.Quantity <= 0 || x.OriginalLineId.HasValue)) return false;
        var facts = all.Where(x => x.SourceDocumentId == head.Uid || rows.Any(l => l.Uid == x.SourceLineId)).ToList();
        if (facts.Count != 2 * rows.Count) return false;
        foreach (var row in rows)
        {
            var originals = facts.Where(x => x.SourceLineId == row.Uid && !x.ReversalOfLedgerId.HasValue).ToList();
            if (originals.Count != 1) return false;
            var original = originals[0];
            var reverse = all.Where(x => x.ReversalOfLedgerId == original.Uid || x.ReplacementOfLedgerId == original.Uid).ToList();
            if (reverse.Count != 1) return false;
            var r = reverse[0];
            if (original.IsDeleted || original.SourceDocumentType != "InboundDocument" || original.SourceDocumentId != head.Uid || original.CommandItemId != row.Uid
                || original.ProductId != row.ProductId || original.WarehouseSiteId != head.WarehouseSiteId || original.QuantityDelta != row.Quantity
                || string.IsNullOrWhiteSpace(row.UnitSnapshot) || original.UnitSnapshot != row.UnitSnapshot
                || original.TransactionType is not (InventoryTransactionTypeEnum.PurchaseReceiptInbound or InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound)
                || original.ReplacementOfLedgerId.HasValue || original.CorrectionGroupId.HasValue
                || r.IsDeleted || r.ReversalOfLedgerId != original.Uid || r.SourceDocumentType != "InboundDocument" || r.SourceDocumentId != head.Uid || r.SourceLineId != row.Uid
                || (int)r.TransactionType != 900 || r.QuantityDelta != -original.QuantityDelta || r.ProductId != original.ProductId
                || r.WarehouseSiteId != original.WarehouseSiteId || r.UnitSnapshot != original.UnitSnapshot || r.ReplacementOfLedgerId.HasValue || r.CorrectionGroupId.HasValue) return false;
        }
        return true;
    }
}
