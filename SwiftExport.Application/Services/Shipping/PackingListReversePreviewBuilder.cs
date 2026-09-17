using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using static SwiftExport.Application.Services.Shipping.PackingListPostingPreviewBuilder;
using Action = SwiftExport.Application.Services.Shipping.PackingListPostingPreviewBuilder.Action;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>只用原执行、原单据和原流水验证整单反向；不读取需求/BOM，不重新分配仓库。</summary>
public sealed class PackingListReversePreviewBuilder
{
    /// <summary>先模拟全部出库恢复，再撤销自动入库；任一证据或余额异常阻断整个方案。</summary>
    public Preview Build(PackingListReverseFacts facts, DateTime businessDate, string reason)
    {
        if (facts.Head == null || facts.Head.IsDeleted || facts.Head.PostingStatus != PackingListPostingStatusEnum.Posted)
            return Fail("INVALID_STATE", "只有已过账箱单可以反过账。");
        if (businessDate == default || string.IsNullOrWhiteSpace(reason)) return Fail("INVALID_COMMAND", "反向日期和原因不能为空。");
        if (facts.Executions.Count != 1 || facts.Executions[0].Operation != 1 || facts.Executions[0].IsDeleted
            || facts.Executions[0].PackingListHeadId != facts.Head.Uid || facts.Executions[0].CommandId != facts.Head.PostCommandId)
            return Fail("EXECUTION_INVALID", "缺少唯一可证明的原正向执行记录。");
        var execution = facts.Executions[0];
        if (facts.Evidence.Count == 0 || facts.Evidence.Any(x => x.IsDeleted || x.ExecutionId != execution.Uid
            || x.Qty <= 0 || x.OriginalExecutionLineId.HasValue
            || x.ActionKind is not ("Delivery" or "Receipt" or "Shipment" or "Consumption")))
            return Fail("EXECUTION_INVALID", "原执行明细缺失或异常。");
        var physical = facts.PackingLines.Where(x => !x.IsDeleted).ToDictionary(x => x.Uid);
        if (facts.Evidence.Any(x => !physical.TryGetValue(x.PackingListLineId, out var line)
            || line.GrossDemandId != x.GrossDemandId)) return Fail("EXECUTION_INVALID", "执行明细与箱单来源归属不一致。");
        foreach (var line in physical.Values)
        {
            var delivered = facts.Evidence.Where(x => x.PackingListLineId == line.Uid && x.ActionKind is "Delivery" or "Shipment").ToArray();
            if (delivered.Sum(x => x.Qty) != line.Qty || delivered.Any(x => x.ProductId != line.ProductId || !SameUnit(x.Unit, line.UnitSnapshot)))
                return Fail("EXECUTION_INVALID", "原交付执行与物理箱单行不一致。");
        }
        var stockEvidence = facts.Evidence.Where(x => x.ActionKind != "Delivery").ToArray();
        if (stockEvidence.Any(x => x.LedgerId == null || x.DocumentLineId == null || x.DocumentId == null
            || x.ProductId == null || x.WarehouseSiteId == null)
            || stockEvidence.Select(x => x.LedgerId).Distinct().Count() != stockEvidence.Length
            || facts.Ledgers.Count != stockEvidence.Length)
            return Fail("LEDGER_INTEGRITY", "原流水缺失、重复或已有反向、更正。");
        if (facts.Evidence.Any(x => x.ActionKind == "Delivery" && (x.LedgerId.HasValue || x.DocumentId.HasValue || x.WarehouseSiteId.HasValue)))
            return Fail("EXECUTION_INVALID", "无库存交付不应具有库存执行关联。");
        var inEvidence = stockEvidence.Where(x => x.ActionKind == "Receipt").ToArray();
        var outEvidence = stockEvidence.Where(x => x.ActionKind != "Receipt").ToArray();
        if (facts.InboundHeads.Count != inEvidence.Select(x => x.DocumentId).Distinct().Count()
            || facts.OutboundHeads.Count != outEvidence.Select(x => x.DocumentId).Distinct().Count()
            || facts.InboundLines.Count != inEvidence.Length || facts.OutboundLines.Count != outEvidence.Length)
            return Fail("DOCUMENT_INTEGRITY", "自动单据与执行记录不完整对应。");
        var balances = new Dictionary<(Guid Warehouse, Guid Product), decimal>();
        foreach (var row in stockEvidence.OrderBy(x => x.ActionKind == "Receipt" ? 1 : 0).ThenBy(x => x.DocumentId).ThenBy(x => x.LineNumber))
        {
            var inbound = row.ActionKind == "Receipt";
            var ledger = facts.Ledgers.SingleOrDefault(x => x.Uid == row.LedgerId);
            var expectedType = inbound ? InventoryTransactionTypeEnum.PackingListAutoReceiptInbound
                : row.ActionKind == "Shipment" ? InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound
                : InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound;
            if (ledger == null || ledger.IsDeleted || ledger.ReversalOfLedgerId.HasValue || ledger.ReplacementOfLedgerId.HasValue
                || ledger.CorrectionGroupId.HasValue || ledger.SourceDocumentId != row.DocumentId || ledger.SourceLineId != row.DocumentLineId
                || ledger.SourceDocumentType != (inbound ? "InboundDocument" : "OutboundDocument")
                || ledger.TransactionType != expectedType || ledger.WarehouseSiteId != row.WarehouseSiteId || ledger.ProductId != row.ProductId
                || ledger.QuantityDelta != (inbound ? row.Qty : -row.Qty) || !SameUnit(ledger.UnitSnapshot, row.Unit))
                return Fail("LEDGER_INTEGRITY", "执行证据与原库存流水不一致。");
            if (inbound)
            {
                var head = facts.InboundHeads.SingleOrDefault(x => x.Uid == row.DocumentId);
                var line = facts.InboundLines.SingleOrDefault(x => x.Uid == row.DocumentLineId);
                if (head == null || head.IsDeleted || head.TriggerPackingListId != facts.Head.Uid
                    || head.CreationMode != InboundCreationModeEnum.Automatic || head.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt
                    || head.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || head.PostingStatus != InboundPostingStatusEnum.Posted
                    || head.Version != row.DocumentVersion || head.WarehouseSiteId != row.WarehouseSiteId
                    || line == null || line.IsDeleted || line.InboundDocumentId != head.Uid || line.ProductId != row.ProductId
                    || line.Quantity != row.Qty || line.SourceLineId != row.PurchaseLineId || line.Uid != ledger.CommandItemId || !SameUnit(line.UnitSnapshot, row.Unit))
                    return Fail("DOCUMENT_INTEGRITY", "原自动入库单据状态、版本或数量不一致。");
            }
            else
            {
                var head = facts.OutboundHeads.SingleOrDefault(x => x.Uid == row.DocumentId);
                var line = facts.OutboundLines.SingleOrDefault(x => x.Uid == row.DocumentLineId);
                var type = row.ActionKind == "Shipment" ? OutboundDocumentTypeEnum.FinalCustomerShipment : OutboundDocumentTypeEnum.CustomerSuppliedConsumption;
                if (head == null || head.IsDeleted || head.TriggerPackingListId != facts.Head.Uid || head.DocumentType != type
                    || head.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || head.PostingStatus != OutboundPostingStatusEnum.Posted
                    || head.Version != row.DocumentVersion || head.WarehouseSiteId != row.WarehouseSiteId
                    || line == null || line.IsDeleted || line.OutboundDocumentId != head.Uid || line.ProductId != row.ProductId
                    || line.Quantity != row.Qty || line.CommandItemId != ledger.CommandItemId || !SameUnit(line.UnitSnapshot, row.Unit))
                    return Fail("DOCUMENT_INTEGRITY", "原自动出库单据状态、版本或数量不一致。");
            }
            var key = (row.WarehouseSiteId!.Value, row.ProductId!.Value);
            if (!balances.TryGetValue(key, out var current))
            {
                var warehouse = facts.Warehouses.SingleOrDefault(x => x.Uid == key.Item1);
                var matching = facts.Balances.Where(x => x.WarehouseSiteId == key.Item1 && x.ProductId == key.Item2).ToArray();
                // 与库存内核历史反向一致：停用仓可恢复历史，但缺失或删除不能猜测。
                if (warehouse == null || warehouse.IsDeleted || matching.Length != 1 || matching[0].IsDeleted || !SameUnit(matching[0].UnitSnapshot, row.Unit))
                    return Fail("BALANCE_INVALID", "原仓库或库存余额缺失、已删除或单位异常。");
                current = matching[0].OnHandQty;
            }
            var after = current - ledger.QuantityDelta;
            if (after < 0) return Fail("NEGATIVE_STOCK", "反向自动入库后库存不足，不能执行普通反过账。");
            balances[key] = after;
        }
        var actions = facts.Evidence.OrderBy(x => x.ActionKind == "Receipt" ? 1 : 0).ThenBy(x => x.LineNumber)
            .Select(x => new Action(x.ActionKind, x.PackingListLineId, x.GrossDemandId, x.ProductId, x.Unit,
                x.WarehouseSiteId, x.PurchaseLineId, x.RootNetDemandId, x.ComponentNetDemandId,
                x.RootQtySnapshot, x.ComponentQtySnapshot, x.ShippedQtySnapshot, x.Qty)).ToArray();
        var content = JsonSerializer.Serialize(new { Rule = "Reverse1", Date = businessDate.Date, Reason = reason.Trim(), facts.Head,
            PackingLines = facts.PackingLines.OrderBy(x => x.Uid), Executions = facts.Executions.OrderBy(x => x.Uid),
            Evidence = facts.Evidence.OrderBy(x => x.Uid), Inbound = facts.InboundHeads.OrderBy(x => x.Uid),
            InboundLines = facts.InboundLines.OrderBy(x => x.Uid), Outbound = facts.OutboundHeads.OrderBy(x => x.Uid),
            OutboundLines = facts.OutboundLines.OrderBy(x => x.Uid), Ledgers = facts.Ledgers.OrderBy(x => x.Uid),
            Balances = facts.Balances.OrderBy(x => x.Uid), Warehouses = facts.Warehouses.OrderBy(x => x.Uid) });
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), actions, []);
    }

    /// <summary>异常时只返回阻断，不提供可部分执行的反向方案。</summary>
    private static Preview Fail(string code, string message) => new(null, [], [new(code, null, message)]);
    /// <summary>沿用原单位，不做历史单位换算。</summary>
    private static bool SameUnit(string? first, string? second) => string.Equals(first?.Trim() ?? "", second?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
}
