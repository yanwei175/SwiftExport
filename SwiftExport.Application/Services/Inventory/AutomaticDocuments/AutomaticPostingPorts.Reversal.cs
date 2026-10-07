using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using System.Text.Json;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Application.Services.Inventory.AutomaticDocuments;

/// <summary>自动单据整单反向；严格证明原单与流水一一对应，不接收数量改写或负库存豁免。</summary>
public sealed partial class AutomaticPostingPorts
{
    private async Task<AutomaticPostingResult> ReverseAsync(AutomaticReverseRequest submitted, bool isInbound, CancellationToken token)
    {
        try
        {
            RequireUser();
            if (submitted is null) Fail(ServiceErrorCodes.Validation, "反向请求不能为空。");
            var request = JsonSerializer.Deserialize<AutomaticReverseRequest>(JsonSerializer.Serialize(submitted))!;
            scope.Require(request.CommandId, request.CorrelationId, request.PackingListId);
            request.Reason = request.Reason?.Trim() ?? "";
            if (request.BusinessDate == default || request.Reason.Length is < 1 or > 500 || request.Documents.Count is < 1 or > 500
                || request.Documents.Any(x => x.DocumentId == Guid.Empty || x.ExpectedVersion <= 0)
                || request.Documents.Select(x => x.DocumentId).Distinct().Count() != request.Documents.Count)
                Fail(ServiceErrorCodes.Validation, "反向日期、原因、单据身份或版本无效。");
            foreach (var document in request.Documents) scope.RequireDocument(document.DocumentId, isInbound);
            var operation = isInbound ? "Inbound.Reverse" : "Outbound.Reverse";
            var fingerprint = Fingerprint(new { UserId = user.UserId, operation, request.CommandId, request.CorrelationId,
                request.PackingListId, request.BusinessDate, request.Reason, Documents = request.Documents.OrderBy(x => x.DocumentId) });
            var prior = await ReplayAsync(request.CommandId, operation, fingerprint);
            if (prior is not null) return prior;
            var result = new AutomaticPostingResult();
            foreach (var document in request.Documents.OrderBy(x => x.DocumentId))
            {
                InboundDocument? ih = null;
                OutboundDocument? oh = null;
                var lines = new List<OriginalLine>();
                string number;
                Guid warehouse;
                InventoryTransactionTypeEnum type;
                if (isInbound)
                {
                    ih = await inbound.LockHeaderAsync(document.DocumentId, token);
                    if (ih is null || ih.IsDeleted || ih.TriggerPackingListId != request.PackingListId
                        || ih.CreationMode != InboundCreationModeEnum.Automatic || ih.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt
                        || ih.PostingStatus != InboundPostingStatusEnum.Posted || ih.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active)
                        Fail(InventoryErrorCodes.DocumentStateInvalid, "原单不是该箱单有效已过账自动入库。", document.DocumentId);
                    if (ih.Version != document.ExpectedVersion) Fail(ServiceErrorCodes.VersionConflict, "自动入库版本已变化。", document.DocumentId);
                    foreach (var l in await inbound.LockLinesAsync(document.DocumentId, token))
                        lines.Add(new(l.Uid, l.ProductId, l.Quantity, l.UnitSnapshot, l.Uid));
                    number = ih.DocumentNo; warehouse = ih.WarehouseSiteId;
                    type = InventoryTransactionTypeEnum.PackingListAutoReceiptInbound;
                }
                else
                {
                    oh = await outbound.LockHeaderAsync(document.DocumentId, token);
                    if (oh is null || oh.IsDeleted || oh.TriggerPackingListId != request.PackingListId
                        || oh.DocumentType is not (OutboundDocumentTypeEnum.FinalCustomerShipment or OutboundDocumentTypeEnum.CustomerSuppliedConsumption)
                        || oh.PostingStatus != OutboundPostingStatusEnum.Posted || oh.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active)
                        Fail(InventoryErrorCodes.DocumentStateInvalid, "原单不是该箱单有效已过账自动出库。", document.DocumentId);
                    if (oh.Version != document.ExpectedVersion) Fail(ServiceErrorCodes.VersionConflict, "自动出库版本已变化。", document.DocumentId);
                    foreach (var l in await outbound.LockLinesAsync(document.DocumentId, token))
                        lines.Add(new(l.Uid, l.ProductId, l.Quantity, l.UnitSnapshot, l.CommandItemId));
                    number = oh.DocumentNo; warehouse = oh.WarehouseSiteId;
                    type = oh.DocumentType == OutboundDocumentTypeEnum.FinalCustomerShipment
                        ? InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound : InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound;
                }
                var facts = isInbound ? await executionReads.ReadReverseLedgersAsync(document.DocumentId, token)
                    : await outboundReads.ReadReverseLedgersAsync(document.DocumentId, token);
                if (lines.Count == 0 || facts.Count != lines.Count || lines.Select(x => x.Id).Distinct().Count() != lines.Count)
                    Fail(InventoryErrorCodes.LedgerIntegrity, "原单流水缺失、重复或已有反向／替代记录。", document.DocumentId);
                var reversals = new List<InventoryReverseItem>();
                foreach (var line in lines)
                {
                    var matching = facts.Where(x => x.SourceLineId == line.Id).ToArray();
                    if (matching.Length != 1) Fail(InventoryErrorCodes.LedgerIntegrity, "原明细不能唯一对应流水。", line.Id);
                    var fact = matching[0];
                    if (line.Quantity <= 0 || decimal.Round(line.Quantity, 4) != line.Quantity || fact.IsDeleted
                        || fact.TransactionType != type || fact.SourceDocumentId != document.DocumentId
                        || fact.SourceDocumentType != (isInbound ? "InboundDocument" : "OutboundDocument")
                        || fact.CommandItemId != line.CommandItemId || fact.WarehouseSiteId != warehouse || fact.ProductId != line.Product
                        || fact.QuantityDelta != (isInbound ? line.Quantity : -line.Quantity) || !SameUnit(fact.UnitSnapshot, line.Unit)
                        || fact.ReversalOfLedgerId.HasValue || fact.ReplacementOfLedgerId.HasValue || fact.CorrectionGroupId.HasValue)
                        Fail(InventoryErrorCodes.LedgerIntegrity, "原明细与库存事实不一致。", line.Id);
                    scope.RequireStock(warehouse, line.Product);
                    scope.RequireLedger(fact.Uid);
                    reversals.Add(new() { CommandItemId = AutomaticPostingScope.ReverseItemId(request.CommandId, fact.Uid),
                        OriginalLedgerId = fact.Uid, SourceDocumentType = fact.SourceDocumentType, SourceDocumentId = document.DocumentId,
                        SourceLineId = line.Id, SourceDocumentNoSnapshot = number, OccurredDate = request.BusinessDate, Reason = request.Reason });
                }
                var reversed = await inventory.ReverseAsync(request.CommandId, reversals, token);
                if (isInbound)
                {
                    ih!.ReversedDate = DateTime.Now; ih.ReversedBy = user.UserName; ih.ReverseReason = request.Reason;
                    await repository.SetInboundStateAsync(ih, true, token);
                }
                else
                {
                    oh!.ReversedDate = DateTime.Now; oh.ReversedBy = user.UserName; oh.ReverseReason = request.Reason;
                    await repository.SetOutboundStateAsync(oh, true, token);
                }
                result.Documents.Add(new() { DocumentId = document.DocumentId, DocumentNo = number, Version = isInbound ? ih!.Version : oh!.Version,
                    PostingStatus = 8, Lines = reversals.Select(x => new AutomaticLineResult(x.SourceLineId!.Value, x.SourceLineId.Value,
                        reversed.Single(r => r.CommandItemId == x.CommandItemId).LedgerId)).ToList() });
            }
            await SaveResultAsync(request.CommandId, request.CorrelationId, request.PackingListId, null, operation, fingerprint, result, request.Reason, token);
            return result;
        }
        // 与正向一致保留PG序列化失败身份；不重新执行反向业务或改变原冲突规则。
        catch (Exception ex) when (scope.IsSerializationFailure(ex)) { scope.Abort(); throw; }
        catch (Exception ex) { scope.Abort(); throw MapFailure(ex); }
    }

    private sealed record OriginalLine(Guid Id, Guid Product, decimal Quantity, string? Unit, Guid CommandItemId);
}
