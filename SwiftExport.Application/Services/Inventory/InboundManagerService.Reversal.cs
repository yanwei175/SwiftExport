using System.Text.Json;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>手工入库整单反过账，保留原凭证与事实，不编排 PackingList 或实际退货。</summary>
public sealed partial class InboundManagerService
{
    /// <inheritdoc/>
    public Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            await RequireReversePermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new DraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await repository.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");
            return (await BuildReversePreviewAsync(detail, request.ExpectedVersion, token)).Preview;
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            await RequireReversePermissionAsync(token);
            ValidateIdentity(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0);
            var reason = command!.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
                throw new DraftException("请填写反过账原因（最多 500 字）。", ServiceErrorCodes.Validation, "Reason");
            if (context.Session.Transaction is not null) throw new DraftException("当前请求已有事务，不能独立反过账。");
            command = new ReverseInboundCommand { CommandId = command.CommandId, ExpectedVersion = command.ExpectedVersion, Reason = reason! };
            var fingerprint = JsonSerializer.Serialize(new { Fingerprint = Fingerprint(uid, command) });
            using var transaction = context.Session.Connection.BeginTransaction();
            context.Session.Transaction = transaction;
            try
            {
                if (await repository.LockCommandAsync(command.CommandId, token) < 0)
                    throw new DraftException("命令正在处理中，请以原命令重试。", InboundErrorCodes.CommandBusy);
                var prior = await logs.GetByModuleCommandAsync(Module, command.CommandId);
                if (prior is not null)
                {
                    if (prior.OperationType != "Reverse" || prior.UserUid != user.UserId || prior.ChangedFieldsJson != fingerprint)
                        throw new DraftException("CommandId 已用于其他内容、操作或用户。", ServiceErrorCodes.CommandConflict);
                    var replay = JsonSerializer.Deserialize<InboundReverseResult>(prior.RelatedObjectsJson ?? "");
                    if (replay is null || replay.Uid != uid || replay.Version <= 0 || replay.PostingStatus != InboundPostingStatusEnum.Reversed)
                        throw new DraftException("历史反过账结果不完整，不能安全重放。", InboundErrorCodes.OutcomeUnknown);
                    transaction.Commit();
                    return replay;
                }
                var initial = await repository.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");
                var sourceIds = initial.Lines.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).ToArray();
                await repository.LockPurchaseSourcesAsync(sourceIds, token);
                var header = await repository.LockHeaderAsync(uid, token) ?? throw new DraftException("入库单不存在。");
                var lockedLines = await repository.LockLinesAsync(uid, token);
                if (!lockedLines.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).SequenceEqual(sourceIds))
                    throw new DraftException("入库来源已变化，请重新加载。", ServiceErrorCodes.VersionConflict);
                var detail = await repository.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");
                var check = await BuildReversePreviewAsync(detail, command.ExpectedVersion, token);
                if (header.Version != command.ExpectedVersion)
                    throw new DraftException("入库版本已变化，请重新加载。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
                if (!check.Preview.CanReverse) throw new DraftException(check.Preview.Blockers[0].Message, check.Preview.Blockers[0].Code);
                var occurred = DateTime.Now;
                // 重放在生成行身份前返回；未提交失败则新身份和整组事实一起回滚。
                var items = check.Originals.OrderBy(x => x.Uid).Select(x => new InventoryReverseItem
                {
                    CommandItemId = GuidSequentialGenerator.NewGuid(), OriginalLedgerId = x.Uid,
                    SourceDocumentType = "InboundDocument", SourceDocumentId = uid, SourceLineId = x.SourceLineId,
                    SourceDocumentNoSnapshot = header.DocumentNo, Reason = command.Reason, OccurredDate = occurred
                }).ToArray();
                try { await inventoryPosting.ReverseAsync(command.CommandId, items, token); }
                catch (InventoryPostingException ex) { throw new DraftException(ex.Message); }
                header.ReversedDate = occurred; header.ReversedBy = user.UserName; header.ReverseReason = command.Reason;
                await repository.MarkReversedAsync(header, token);
                var result = new InboundReverseResult { Uid = uid, Version = header.Version, DocumentNo = header.DocumentNo,
                    PostingStatus = InboundPostingStatusEnum.Reversed, ReversedDate = occurred, ReversedBy = user.UserName, ReverseReason = command.Reason };
                if (await logs.InsertAsync(new BusinessOperationLog
                {
                    CommandId = command.CommandId, CorrelationId = command.CommandId.ToString("N"), Module = Module,
                    AggregateType = "InboundDocument", AggregateUid = uid, AggregateNoSnapshot = header.DocumentNo,
                    OperationType = "Reverse", OperationName = "手工入库反过账", Summary = "整单扣减库存，原单保留已反过账只读。",
                    Reason = command.Reason, ChangedFieldsJson = fingerprint, RelatedObjectsJson = JsonSerializer.Serialize(result),
                    UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = occurred
                }) != 1) throw new DraftException("反过账日志写入失败，整单已回滚。");
                token.ThrowIfCancellationRequested();
                transaction.Commit(); return result;
            }
            catch { try { transaction.Rollback(); } catch { } throw; }
            finally { context.Session.Transaction = null; }
        }, token);

    /// <summary>反过账是独立授权，不以正向权限或角色名代替。</summary>
    private async Task RequireReversePermissionAsync(CancellationToken token)
    {
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryInboundReverse, token))
            throw new DraftException("当前账号没有手工入库反过账权限。", ServiceErrorCodes.Forbidden);
    }

    /// <summary>以原库存事实证明整单可撤销；只按仓库产品余额判断，不检查后续出库或采购收货资格。</summary>
    private async Task<(InboundReversePreviewDto Preview, IReadOnlyList<InventoryLedgerEntry> Originals)> BuildReversePreviewAsync(
        InboundManagerDetailDto detail, int version, CancellationToken token)
    {
        var h = detail.Header;
        var preview = new InboundReversePreviewDto { Uid = h.Uid, Version = h.Version, DocumentNo = h.DocumentNo, WarehouseName = h.WarehouseNameSnapshot };
        void Block(string message, string code = ServiceErrorCodes.BusinessBlocked, Guid? line = null) =>
            preview.Blockers.Add(new() { Message = message, Code = code, LineId = line });
        if (h.Version != version) Block("入库版本已变化，请重新加载。", ServiceErrorCodes.VersionConflict);
        if (h.IsDeleted || h.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || h.PostingStatus != InboundPostingStatusEnum.Posted
            || h.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt || h.CreationMode != InboundCreationModeEnum.Manual || h.TriggerPackingListId.HasValue)
            Block("仅允许活动、人工、已过账且非 PackingList 触发的采购入库整单反过账。");
        if (preview.Blockers.Count > 0) return (preview, []);
        var rows = detail.Lines;
        if (rows.Count is < 1 or > 500 || rows.Select(x => x.Uid).Distinct().Count() != rows.Count)
        { Block("原入库明细缺失或身份重复，不能证明整单反向。"); return (preview, []); }
        var facts = await repository.ReadReverseLedgersAsync(h.Uid, token);
        if (facts.Count != rows.Count) Block("原流水缺失、重复或存在额外、反向、替代事实，禁止部分撤销。");
        foreach (var row in rows)
        {
            var matches = facts.Where(x => x.SourceLineId == row.Uid).ToArray();
            if (row.Uid == Guid.Empty || row.ProductId == Guid.Empty || row.SourceLineId == Guid.Empty || row.InboundDocumentId != h.Uid
                || row.OriginalLineId.HasValue || row.Quantity <= 0 || decimal.Round(row.Quantity, 4) != row.Quantity || matches.Length != 1)
            { Block($"第 {row.LineNumber} 行无法唯一对应原库存流水。", line: row.Uid); continue; }
            var original = matches[0];
            if (original.IsDeleted || original.TransactionType is not (InventoryTransactionTypeEnum.PurchaseReceiptInbound or InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound)
                || original.SourceDocumentType != "InboundDocument" || original.SourceDocumentId != h.Uid
                || original.CommandItemId != row.Uid || original.WarehouseSiteId != h.WarehouseSiteId || original.ProductId != row.ProductId
                || original.QuantityDelta != row.Quantity || string.IsNullOrWhiteSpace(original.UnitSnapshot)
                || !string.Equals(original.UnitSnapshot.Trim(), row.UnitSnapshot?.Trim(), StringComparison.OrdinalIgnoreCase)
                || original.ReversalOfLedgerId.HasValue || original.ReplacementOfLedgerId.HasValue || original.CorrectionGroupId.HasValue)
            { Block($"第 {row.LineNumber} 行原流水身份、数量、单位或纠错关系异常。", line: row.Uid); continue; }
            preview.Ledgers.Add(new() { LedgerId = original.Uid, LineId = row.Uid, ProductId = original.ProductId,
                Sku = original.SkuSnapshot, Unit = original.UnitSnapshot, Quantity = original.QuantityDelta });
        }
        foreach (var group in preview.Ledgers.GroupBy(x => x.ProductId))
        {
            var first = group.First();
            var balance = await repository.ReadReverseBalanceAsync(h.WarehouseSiteId, group.Key, token);
            if (balance is null || balance.IsDeleted || group.Any(x => !string.Equals(x.Unit?.Trim(), first.Unit?.Trim(), StringComparison.OrdinalIgnoreCase))
                || !string.Equals(balance.UnitSnapshot?.Trim(), first.Unit?.Trim(), StringComparison.OrdinalIgnoreCase))
            { Block($"产品 {first.Sku} 的原余额投影缺失、已删除或单位异常。"); continue; }
            var quantity = group.Sum(x => x.Quantity);
            var after = balance.OnHandQty - quantity;
            if (after < 0 || after > 99999999999999.9999m) Block($"产品 {first.Sku} 当前库存 {balance.OnHandQty:0.####}，需要扣减 {quantity:0.####}，余额不足或超出范围。");
            preview.StockImpacts.Add(new() { WarehouseSiteId = h.WarehouseSiteId, ProductId = group.Key, Sku = first.Sku, Unit = first.Unit,
                OriginalQuantityDelta = quantity, ReverseQuantity = quantity, CurrentQuantity = balance.OnHandQty, ExpectedQuantity = after });
        }
        if (!await repository.ReverseWarehouseExistsAsync(h.WarehouseSiteId, token)) Block("原仓库不存在或已删除。");
        preview.CanReverse = preview.Blockers.Count == 0;
        return (preview, facts);
    }
}
