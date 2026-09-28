using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using System.Text.Json;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>手工入库整单反过账，保留原凭证与事实，不编排 PackingList 或实际退货。</summary>
public sealed class InboundReversalService(
    IInboundDocumentRepository repository, IInboundExecutionReadRepository executionReads, IInboundQueryRepository queries, IInboundSourceRepository sourceRepository, IBusinessOperationLogRepository logs, ICurrentUser user,
    IPermissionEvaluationService permissions, IInventoryPostingService inventoryPosting,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    InboundServiceSupport support) : IInboundReversalService
{
    private const string Module = "InboundManager";
    /// <inheritdoc/>
    public Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            await RequireReversePermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new DraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await queries.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");
            return (await BuildReversePreviewAsync(detail, request.ExpectedVersion, token)).Preview;
        }, token);

    /// <summary>通过公共管道执行整单反过账，相同命令重试返回首次结果，不重复扣减库存。</summary>
    public Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token = default) =>
        // 管道先授权、再验证请求，最后记录结果和耗时；通过验证后才进入命令执行方法。
        pipeline.ExecuteAsync<ReverseInboundCommand, InboundReverseResult>(command, command?.CommandId,
            "Inventory.Inbound.Reverse", AuthorizeReverseAsync<InboundReverseResult>,
            ct => ExecuteReverseCommandAsync(uid, command!, ct), token);

    /// <summary>校验路由身份并交给公共执行器管理事务和回执；已有成功回执时跳过业务方法。</summary>
    private Task<ServiceResult<CommandExecutionResult<InboundReverseResult>>> ExecuteReverseCommandAsync(
        Guid uid, ReverseInboundCommand command, CancellationToken token) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("单据标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            // 验证器已检查原因；规范化内容同时用于指纹、库存事实和审计。
            var reason = command.Reason.Trim();
            // 目标单据和业务输入参与指纹校验，防止同一 CommandId 被用于不同请求。
            return await executor.ExecuteAsync(command.CommandId, "Inventory.Inbound.Reverse", uid,
                new { command.ExpectedVersion, Reason = reason }, ct => ReverseInTransactionAsync(uid, command, reason, ct), token);
        }, token, checkPermission: false); // 管道已检查权限，此处保留模块错误包装。

    /// <summary>
    /// 在公共执行器拥有的事务内完成首次业务执行：锁定、校验、库存写入、单据更新和业务审计。
    /// 本方法不自行提交事务；异常交给执行器回滚，成功结果由执行器保存回执后提交。
    /// </summary>
    private async Task<InboundReverseResult> ReverseInTransactionAsync(Guid uid, ReverseInboundCommand command, string reason, CancellationToken token)
    {
        // 1. 在执行器已开启的事务内，按采购来源、入库头、入库行顺序取得业务锁。
        var initial = await queries.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");

        var sourceIds = initial.Lines.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).ToArray();

        await sourceRepository.LockPurchaseSourcesAsync(sourceIds, token);

        var header = await repository.LockHeaderAsync(uid, token) ?? throw new DraftException("入库单不存在。");

        var lockedLines = await repository.LockLinesAsync(uid, token);

        if (!lockedLines.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).SequenceEqual(sourceIds))
            throw new DraftException("入库来源已变化，请重新加载。", ServiceErrorCodes.VersionConflict);

        // 2. 锁定后重新检查版本和整单反向条件，不沿用此前预览结论。
        var detail = await queries.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在。");
        var check = await BuildReversePreviewAsync(detail, command.ExpectedVersion, token);

        if (header.Version != command.ExpectedVersion)
            throw new DraftException("入库版本已变化，请重新加载。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");

        if (!check.Preview.CanReverse)
            throw new DraftException(check.Preview.Blockers[0].Message, check.Preview.Blockers[0].Code);

        // 3. 根据原流水生成反向事实；保留原流水，命令重放不会进入此方法。
        var occurred = DateTime.Now;
        // 仅首次执行进入业务委托并生成行身份；失败时整组反向事实随外层事务回滚。
        var items = check.Originals.OrderBy(x => x.Uid).Select(x => new InventoryReverseItem
        {
            CommandItemId = GuidSequentialGenerator.NewGuid(),
            OriginalLedgerId = x.Uid,
            SourceDocumentType = "InboundDocument",
            SourceDocumentId = uid,
            SourceLineId = x.SourceLineId,
            SourceDocumentNoSnapshot = header.DocumentNo,
            Reason = reason,
            OccurredDate = occurred
        }).ToArray();

        try
        {
            await inventoryPosting.ReverseAsync(command.CommandId, items, token);
        }
        catch (InventoryPostingException ex)
        {
            throw new DraftException(ex.Message, ex.Code);
        }

        // 4. 更新反过账状态、操作人及原因，仓储同步递增版本。
        header.ReversedDate = occurred; header.ReversedBy = user.UserName; header.ReverseReason = reason;

        await repository.MarkReversedAsync(header, token);

        var result = new InboundReverseResult
        {
            Uid = uid,
            Version = header.Version,
            DocumentNo = header.DocumentNo,
            PostingStatus = InboundPostingStatusEnum.Reversed,
            ReversedDate = occurred,
            ReversedBy = user.UserName,
            ReverseReason = reason
        };
        // 5. 写业务审计；失败交给执行器回滚库存、单据和审计。
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            CommandId = command.CommandId,
            CorrelationId = correlation.CorrelationId,
            Module = Module,
            AggregateType = "InboundDocument",
            AggregateUid = uid,
            AggregateNoSnapshot = header.DocumentNo,
            OperationType = "Reverse",
            OperationName = "手工入库反过账",
            Summary = "整单扣减库存，原单保留已反过账只读。",
            Reason = reason,
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                PostingStatus = new { Before = InboundPostingStatusEnum.Posted, After = InboundPostingStatusEnum.Reversed },
                Version = new { Before = command.ExpectedVersion, After = header.Version },
                header.ReversedDate,
                header.ReversedBy,
                ReverseReason = reason
            }),
            RelatedObjectsJson = JsonSerializer.Serialize(new
            {
                InboundUid = uid,
                OriginalLedgerIds = check.Originals.Select(x => x.Uid).ToArray(),
                ReverseCommandItemIds = items.Select(x => x.CommandItemId).ToArray()
            }),
            UserUid = user.UserId,
            UserNameSnapshot = user.UserName,
            OccurredDate = occurred
        }) != 1) throw new DraftException("反过账日志写入失败，整单已回滚。");

        // 6. 业务完成后由执行器保存回执并统一提交事务。
        return result;
    }

    /// <summary>在校验和回执读取前核对库存模块与反向细权限。</summary>
    private async Task<ServiceResult<T>?> AuthorizeReverseAsync<T>(CancellationToken token)
    {
        var refusal = await support.AuthorizeAsync<T>(token, PermissionKeys.InventoryInboundReverse);
        if (refusal is not null) return refusal;
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryInboundReverse, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有手工入库反过账权限。");
        return null;
    }

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
        var facts = await executionReads.ReadReverseLedgersAsync(h.Uid, token);
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
            preview.Ledgers.Add(new()
            {
                LedgerId = original.Uid,
                LineId = row.Uid,
                ProductId = original.ProductId,
                Sku = original.SkuSnapshot,
                Unit = original.UnitSnapshot,
                Quantity = original.QuantityDelta
            });
        }
        foreach (var group in preview.Ledgers.GroupBy(x => x.ProductId))
        {
            var first = group.First();
            var balance = await executionReads.ReadReverseBalanceAsync(h.WarehouseSiteId, group.Key, token);
            if (balance is null || balance.IsDeleted || group.Any(x => !string.Equals(x.Unit?.Trim(), first.Unit?.Trim(), StringComparison.OrdinalIgnoreCase))
                || !string.Equals(balance.UnitSnapshot?.Trim(), first.Unit?.Trim(), StringComparison.OrdinalIgnoreCase))
            { Block($"产品 {first.Sku} 的原余额投影缺失、已删除或单位异常。"); continue; }
            var quantity = group.Sum(x => x.Quantity);
            var after = balance.OnHandQty - quantity;
            if (after < 0 || after > 99999999999999.9999m) Block($"产品 {first.Sku} 当前库存 {balance.OnHandQty:0.####}，需要扣减 {quantity:0.####}，余额不足或超出范围。");
            preview.StockImpacts.Add(new()
            {
                WarehouseSiteId = h.WarehouseSiteId,
                ProductId = group.Key,
                Sku = first.Sku,
                Unit = first.Unit,
                OriginalQuantityDelta = quantity,
                ReverseQuantity = quantity,
                CurrentQuantity = balance.OnHandQty,
                ExpectedQuantity = after
            });
        }
        if (!await executionReads.ReverseWarehouseExistsAsync(h.WarehouseSiteId, token)) Block("原仓库不存在或已删除。");
        preview.CanReverse = preview.Blockers.Count == 0;
        return (preview, facts);
    }
}
