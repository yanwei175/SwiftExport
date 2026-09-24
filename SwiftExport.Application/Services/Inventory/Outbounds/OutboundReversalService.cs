using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
namespace SwiftExport.Application.Services.Inventory.Outbounds;

/// <summary>手工出库整单反过账，保留原凭证与事实，不编排 PackingList 或实际退货。</summary>
public sealed class OutboundReversalService(IOutboundQueryRepository queries, IOutboundDocumentRepository repository,
    IOutboundExecutionReadRepository executionReads, IBusinessOperationLogRepository logs,
    ICurrentUser user, IUserPermissionService permissions, IInventoryPostingService inventoryPosting,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    OutboundServiceSupport support) : IOutboundReversalService
{
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, OutboundReversePreviewRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            await RequireReversePermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new OutboundDraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await queries.GetDetailAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在。");
            return (await BuildReversePreviewAsync(detail, request.ExpectedVersion, token)).Preview;
        }, token);

    /// <summary>经公共管道执行手工出库整单反向，同命令重试返回首次回执。</summary>
    public Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, ReverseOutboundCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<ReverseOutboundCommand, OutboundReverseResult>(command, command?.CommandId,
            "Inventory.Outbound.Reverse", AuthorizeReverseAsync<OutboundReverseResult>,
            ct => ExecuteReverseCommandAsync(uid, command!, ct), token);

    /// <summary>校验路由身份并交给执行器管理事务和回执；反向原因保持现有输入语义。</summary>
    private Task<ServiceResult<CommandExecutionResult<OutboundReverseResult>>> ExecuteReverseCommandAsync(
        Guid uid, ReverseOutboundCommand command, CancellationToken token) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new OutboundDraftException("单据标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await executor.ExecuteAsync(command.CommandId, "Inventory.Outbound.Reverse", uid,
                new { command.ExpectedVersion, command.Reason },
                ct => ReverseInTransactionAsync(uid, command, ct), token);
        }, token, checkPermission: false);

    /// <summary>在执行器事务内证明原流水完整，再恢复库存、更新状态及写业务审计。</summary>
    private async Task<OutboundReverseResult> ReverseInTransactionAsync(Guid uid, ReverseOutboundCommand command, CancellationToken token)
    {
        var header = await repository.LockHeaderAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在。");
        await repository.LockLinesAsync(uid, token);
        var detail = await queries.GetDetailAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在。");
        var check = await BuildReversePreviewAsync(detail, command.ExpectedVersion, token);
        if (header.Version != command.ExpectedVersion)
            throw new OutboundDraftException("出库版本已变化，请重新加载。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
        if (!check.Preview.CanReverse) throw new OutboundDraftException(string.Join("；", check.Preview.Blockers));
        var occurred = DateTime.Now;
        var items = check.Originals.OrderBy(x => x.Uid).Select(x => new InventoryReverseItem
        {
            CommandItemId = GuidSequentialGenerator.NewGuid(), OriginalLedgerId = x.Uid,
            SourceDocumentType = "OutboundDocument", SourceDocumentId = uid, SourceLineId = x.SourceLineId,
            SourceDocumentNoSnapshot = header.DocumentNo, Reason = command.Reason, OccurredDate = occurred
        }).ToArray();
        try { await inventoryPosting.ReverseAsync(command.CommandId, items, token); }
        catch (InventoryPostingException ex) { throw new OutboundDraftException(ex.Message, ex.Code); }
        header.ReversedDate = occurred; header.ReversedBy = user.UserName; header.ReverseReason = command.Reason;
        await repository.MarkReversedAsync(header, token);
        var result = new OutboundReverseResult { Uid = uid, Version = header.Version, DocumentNo = header.DocumentNo,
            PostingStatus = OutboundPostingStatusEnum.Reversed, ReversedDate = occurred, ReversedBy = user.UserName, ReverseReason = command.Reason };
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            CommandId = command.CommandId, CorrelationId = correlation.CorrelationId, Module = "OutboundManager",
            AggregateType = "OutboundDocument", AggregateUid = uid, AggregateNoSnapshot = header.DocumentNo,
            OperationType = "Reverse", OperationName = "手工出库反过账", Summary = "整单恢复库存，原单保留已反过账只读。",
            Reason = command.Reason,
            ChangedFieldsJson = JsonSerializer.Serialize(new { PostingStatus = new { Before = OutboundPostingStatusEnum.Posted, After = OutboundPostingStatusEnum.Reversed }, command.ExpectedVersion, header.Version, header.ReversedDate, header.ReversedBy, header.ReverseReason }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { result.Uid, result.DocumentNo, OriginalLedgerIds = check.Originals.Select(x => x.Uid).ToArray(), ReverseCommandItemIds = items.Select(x => x.CommandItemId).ToArray() }),
            UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = occurred
        }) != 1) throw new OutboundDraftException("反过账日志写入失败，整单已回滚。");
        return result;
    }

    /// <summary>在请求验证和回执读取前检查库存模块与独立反向权限。</summary>
    private async Task<ServiceResult<T>?> AuthorizeReverseAsync<T>(CancellationToken token)
    {
        var refusal = await support.AuthorizeAsync<T>(token);
        if (refusal is not null) return refusal;
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryOutboundReverse, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有手工出库反过账权限。");
        return null;
    }
    /// <summary>反过账是独立授权，不以正向权限或角色名代替。</summary>
    private async Task RequireReversePermissionAsync(CancellationToken token)
    {
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryOutboundReverse, token))
            throw new OutboundDraftException("当前账号没有手工出库反过账权限。", ServiceErrorCodes.Forbidden);
    }

    /// <summary>只读预览与锁头后的提交共用整单证明；最终原流水和余额锁由库存内核处理。</summary>
    private async Task<(OutboundReversePreviewDto Preview, IReadOnlyList<InventoryLedgerEntry> Originals)> BuildReversePreviewAsync(
        OutboundManagerDetailDto detail, int version, CancellationToken token)
    {
        var h = detail.Header;
        var preview = new OutboundReversePreviewDto { Uid = h.Uid, Version = h.Version, DocumentNo = h.DocumentNo,
            WarehouseName = h.WarehouseNameSnapshot, IssueReason = h.IssueReason };
        if (h.Version != version) preview.Blockers.Add("出库版本已变化，请重新加载。");
        if (h.IsDeleted || h.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || h.PostingStatus != OutboundPostingStatusEnum.Posted
            || h.DocumentType != OutboundDocumentTypeEnum.StockIssue || h.TriggerPackingListId.HasValue)
            preview.Blockers.Add("仅允许活动、已过账且非 PackingList 触发的手工库存出库整单反过账。");
        if (preview.Blockers.Count > 0) return (preview, []);
        var rows = detail.Lines;
        if (rows.Count is < 1 or > 500 || rows.Select(x => x.Uid).Distinct().Count() != rows.Count
            || rows.Select(x => x.ProductId).Distinct().Count() != rows.Count || rows.Select(x => x.CommandItemId).Distinct().Count() != rows.Count)
        { preview.Blockers.Add("原出库明细缺失或身份重复，不能证明整单反向。"); return (preview, []); }
        var facts = await executionReads.ReadReverseLedgersAsync(h.Uid, token);
        if (facts.Count != rows.Count) preview.Blockers.Add("原流水缺失、重复或存在额外/反向/替代事实，禁止部分恢复。");
        foreach (var row in rows)
        {
            var matches = facts.Where(x => x.SourceLineId == row.Uid).ToArray();
            if (row.Uid == Guid.Empty || row.CommandItemId == Guid.Empty || row.ProductId == Guid.Empty || row.OutboundDocumentId != h.Uid
                || row.OriginalLineId.HasValue || row.Quantity <= 0 || decimal.Round(row.Quantity,4) != row.Quantity || matches.Length != 1)
            { preview.Blockers.Add($"第 {row.LineNumber} 行无法唯一对应原库存流水。"); continue; }
            var original = matches[0];
            if (original.IsDeleted || original.TransactionType != InventoryTransactionTypeEnum.StockIssueOutbound
                || original.SourceDocumentType != "OutboundDocument" || original.SourceDocumentId != h.Uid
                || original.CommandItemId != row.CommandItemId || original.WarehouseSiteId != h.WarehouseSiteId || original.ProductId != row.ProductId
                || original.QuantityDelta != -row.Quantity || string.IsNullOrWhiteSpace(original.UnitSnapshot)
                || !string.Equals(original.UnitSnapshot.Trim(), row.UnitSnapshot?.Trim(), StringComparison.OrdinalIgnoreCase)
                || original.ReversalOfLedgerId.HasValue || original.ReplacementOfLedgerId.HasValue || original.CorrectionGroupId.HasValue)
            { preview.Blockers.Add($"第 {row.LineNumber} 行原流水身份、数量、单位或纠错关系异常。"); continue; }
            var balance = await executionReads.ReadReverseBalanceAsync(original.WarehouseSiteId, original.ProductId, token);
            if (balance is null || balance.IsDeleted || !string.Equals(balance.UnitSnapshot?.Trim(), original.UnitSnapshot.Trim(), StringComparison.OrdinalIgnoreCase))
            { preview.Blockers.Add($"产品 {row.SkuSnapshot} 的原余额投影缺失、已删除或单位异常。"); continue; }
            var after = balance.OnHandQty - original.QuantityDelta;
            if (after < 0 || after > 99999999999999.9999m) preview.Blockers.Add($"产品 {row.SkuSnapshot} 恢复后库存超出允许范围。");
            preview.StockImpacts.Add(new() { LineId = row.Uid, ProductId = original.ProductId, Sku = original.SkuSnapshot, Unit = original.UnitSnapshot,
                OriginalQuantityDelta = original.QuantityDelta, RestoreQuantity = -original.QuantityDelta, CurrentQuantity = balance.OnHandQty, ExpectedQuantity = after });
        }
        if (!await executionReads.ReverseWarehouseExistsAsync(h.WarehouseSiteId, token)) preview.Blockers.Add("原仓库不存在或已删除。");
        preview.CanReverse = preview.Blockers.Count == 0;
        return (preview, facts);
    }
}
