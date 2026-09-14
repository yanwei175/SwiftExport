using System.Data;
using System.Text.Json;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>正向出库过账编排；普通库存出库不消费需求或采购来源，库存内核只关联出库凭证。</summary>
public sealed partial class OutboundManagerService
{
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            await RequirePostingPermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new DraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await repository.GetDetailAsync(uid, token) ?? throw new DraftException("出库单不存在或已删除。");
            return await BuildPostingPreviewAsync(detail, request.ExpectedVersion, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token = default) =>
        RunAsync(async () =>
        {
            await RequirePostingPermissionAsync(token);
            ValidateIdentity(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0);
            if (context.Session.Transaction is not null) throw new DraftException("当前请求已有事务，不能独立过账。");
            var fingerprint = JsonSerializer.Serialize(new { Fingerprint = Fingerprint(uid, command!) });
            using var transaction = context.Session.Connection.BeginTransaction();
            context.Session.Transaction = transaction;
            try
            {
                if (await repository.LockCommandAsync(command!.CommandId, token) < 0)
                    throw new DraftException("出库命令正在处理中，请以原命令重试。", OutboundErrorCodes.CommandBusy);
                var prior = await logs.GetByModuleCommandAsync(Module, command.CommandId);
                if (prior is not null)
                {
                    if (prior.OperationType != "Post" || prior.UserUid != user.UserId || prior.ChangedFieldsJson != fingerprint)
                        throw new DraftException("CommandId 已用于其他内容、操作或用户。", ServiceErrorCodes.CommandConflict);
                    var replay = JsonSerializer.Deserialize<OutboundPostResult>(prior.RelatedObjectsJson ?? "");
                    if (replay is null || replay.Uid != uid || replay.Version <= 0 || replay.PostingStatus != OutboundPostingStatusEnum.Posted)
                        throw new DraftException("历史过账结果不完整，不能安全重放。", OutboundErrorCodes.OutcomeUnknown);
                    transaction.Commit();
                    return replay;
                }

                var header = await RequireEditableAsync(uid, command.ExpectedVersion, token);
                var rows = await repository.LockLinesAsync(uid, token);
                var detail = await repository.GetDetailAsync(uid, token) ?? throw new DraftException("出库单不存在。");
                var preview = await BuildPostingPreviewAsync(detail, command.ExpectedVersion, token);
                if (!preview.CanPost)
                {
                    var first = preview.Blockers[0];
                    throw new DraftException(first.Message, first.Code, first.LineId.HasValue ? $"Lines[{first.LineId}].Quantity" : null);
                }
                var items = rows.OrderBy(x => x.Uid).Select(x => new InventoryPostingItem
                {
                    CommandItemId = x.CommandItemId, WarehouseSiteId = header.WarehouseSiteId, ProductId = x.ProductId,
                    SkuSnapshot = x.SkuSnapshot, ProductNameSnapshot = x.DescriptionSnapshot, UnitSnapshot = x.UnitSnapshot,
                    QuantityDelta = -x.Quantity, TransactionType = InventoryTransactionTypeEnum.StockIssueOutbound,
                    SourceDocumentType = "OutboundDocument", SourceDocumentId = uid, SourceLineId = x.Uid,
                    SourceDocumentNoSnapshot = header.DocumentNo, OccurredDate = header.ShipmentDate
                }).ToArray();
                try { await inventoryPosting.PostAsync(command.CommandId, items, token); }
                catch (InventoryPostingException ex) { throw new DraftException(ex.Message); }
                header.PostedDate = DateTime.Now;
                header.PostedBy = user.UserName;
                await repository.MarkPostedAsync(header, token);
                var result = new OutboundPostResult { Uid = uid, Version = header.Version, DocumentNo = header.DocumentNo, PostingStatus = OutboundPostingStatusEnum.Posted };
                if (await logs.InsertAsync(new BusinessOperationLog
                {
                    CommandId = command.CommandId, CorrelationId = command.CommandId.ToString("N"), Module = Module,
                    AggregateType = "OutboundDocument", AggregateUid = uid, AggregateNoSnapshot = header.DocumentNo,
                    OperationType = "Post", OperationName = "出库过账", Summary = "出库状态、库存事实及余额同事务提交。",
                    ChangedFieldsJson = fingerprint, RelatedObjectsJson = JsonSerializer.Serialize(result),
                    UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = header.PostedDate.Value
                }) != 1) throw new DraftException("过账日志写入失败，过账已回滚。");
                token.ThrowIfCancellationRequested();
                transaction.Commit();
                return result;
            }
            catch
            {
                // 库存内核可能已回滚，不能让二次回滚异常覆盖原始失败类别。
                try { transaction.Rollback(); } catch { }
                throw;
            }
            finally { context.Session.Transaction = null; }
        }, token);

    private async Task RequirePostingPermissionAsync(CancellationToken token)
    {
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryOutboundPost, token))
            throw new DraftException("当前账号没有出库过账权限。", ServiceErrorCodes.Forbidden);
    }

    /// <summary>预览不预留库存；正式提交再次检查，最终负库存门禁由统一库存锁保护。</summary>
    private async Task<OutboundPostPreviewDto> BuildPostingPreviewAsync(OutboundManagerDetailDto detail, int version, CancellationToken token)
    {
        var h = detail.Header;
        var preview = new OutboundPostPreviewDto { Uid = h.Uid, Version = h.Version, DocumentNo = h.DocumentNo,
            WarehouseSiteId = h.WarehouseSiteId, WarehouseName = h.WarehouseNameSnapshot };
        void Block(string message, string code = ServiceErrorCodes.BusinessBlocked) => preview.Blockers.Add(new() { Message = message, Code = code });
        if (h.Version != version) Block("出库版本已变化，请重新加载后预览。", ServiceErrorCodes.VersionConflict);
        if (h.PostingStatus != OutboundPostingStatusEnum.Unposted || h.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active
            || h.DocumentType != OutboundDocumentTypeEnum.StockIssue || h.TriggerPackingListId.HasValue || h.IsDeleted)
            Block("只有活动、普通库存出库且非 PL 触发的草稿可以过账。");
        if (detail.Lines.Count is < 1 or > 500 || detail.Lines.Any(x => x.Uid == Guid.Empty || x.CommandItemId == Guid.Empty
            || x.ProductId == Guid.Empty || x.OriginalLineId.HasValue || x.Quantity <= 0 || x.Quantity > 99999999999999.9999m
            || decimal.Round(x.Quantity, 4) != x.Quantity || string.IsNullOrWhiteSpace(x.UnitSnapshot))
            || detail.Lines.Select(x => x.ProductId).Distinct().Count() != detail.Lines.Count
            || detail.Lines.Select(x => x.CommandItemId).Distinct().Count() != detail.Lines.Count)
        {
            Block("出库明细身份、数量、单位或重复产品校验失败，请修改草稿。", ServiceErrorCodes.Validation);
            return preview;
        }
        if (h.ShipmentDate == default || string.IsNullOrWhiteSpace(h.DocumentNo)) Block("出库日期或单号无效。", ServiceErrorCodes.Validation);
        if (await repository.HasLedgerAsync(h.Uid, token)) Block("该单已有正式库存流水，不能再次过账。");
        var name = await repository.ReadActiveWarehouseNameAsync(h.WarehouseSiteId, token);
        if (name is null) Block("仓库不存在、已删除或已停用。"); else preview.WarehouseName = name;
        var products = (await repository.GetProductsAsync(detail.Lines.Select(x => x.ProductId).ToArray(), token)).ToDictionary(x => x.ProductId);
        foreach (var line in detail.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || string.IsNullOrWhiteSpace(product.Unit)
                || !string.Equals(line.UnitSnapshot.Trim(), product.Unit.Trim(), StringComparison.OrdinalIgnoreCase))
                Block($"产品 {line.SkuSnapshot} 已删除或单位与快照不一致。");
            var current = await repository.ReadBalanceAsync(h.WarehouseSiteId, line.ProductId, token);
            var after = current - line.Quantity;
            preview.StockImpacts.Add(new() { ProductId = line.ProductId, Sku = line.SkuSnapshot,
                CurrentQuantity = current, QuantityDelta = -line.Quantity, ExpectedQuantity = after });
            if (after < 0) preview.Blockers.Add(new() { LineId = line.Uid, Code = ServiceErrorCodes.BusinessBlocked,
                Message = $"产品 {line.SkuSnapshot} 库存不足：现有 {current}，本次出库 {line.Quantity}。" });
        }
        preview.CanPost = preview.Blockers.Count == 0;
        return preview;
    }
}
