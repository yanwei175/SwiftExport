using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>正向入库过账编排；来源校验按入库类型选择，库存内核只关联入库凭证。</summary>
public sealed class InboundPostingService(
    IInboundManagerRepository repository, IBusinessOperationLogRepository logs, ICurrentUser user,
    IUserPermissionService permissions, IInventoryPostingService inventoryPosting,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    InboundServiceSupport support) : IInboundPostingService
{
    private const string Module = "InboundManager";
    /// <inheritdoc/>
    public Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            await RequirePostingPermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new DraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await repository.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");
            return await BuildPostingPreviewAsync(detail, request.ExpectedVersion, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<PostInboundCommand, InboundPostResult>(command, command?.CommandId,
            "Inventory.Inbound.Post", AuthorizePostingAsync<InboundPostResult>, ct => support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("单据标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await executor.ExecuteAsync(command!.CommandId, "Inventory.Inbound.Post", uid,
                new { command.ExpectedVersion }, async executionToken =>
            {
                // 公共执行器已取得命令锁；业务锁仍按采购来源、入库头行的固定顺序获取。
                var initial = await repository.GetDetailAsync(uid, executionToken) ?? throw new DraftException("入库单不存在或已删除。");
                if (initial.Header.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
                    throw new DraftException("当前仅支持采购入库过账，其他来源类型尚未实现。");
                var ids = initial.Lines.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).ToArray();
                await repository.LockPurchaseSourcesAsync(ids, executionToken);
                var header = await repository.LockHeaderAsync(uid, executionToken) ?? throw new DraftException("入库单不存在或已删除。");
                var rows = await repository.LockLinesAsync(uid, executionToken);
                if (header.Version != command.ExpectedVersion || !rows.Select(x => x.SourceLineId).Distinct().OrderBy(x => x).SequenceEqual(ids))
                    throw new DraftException("入库版本或来源已变化，请重新加载后预览。", ServiceErrorCodes.VersionConflict);
                var detail = await repository.GetDetailAsync(uid, executionToken) ?? throw new DraftException("入库单不存在。");
                var preview = await BuildPostingPreviewAsync(detail, command.ExpectedVersion, executionToken);
                if (!preview.CanPost)
                {
                    var first = preview.Blockers[0];
                    throw new DraftException(first.Message, first.Code, first.LineId.HasValue ? $"Lines[{first.LineId}].Quantity" : null);
                }
                var sources = (await repository.ReadPostingSourcesAsync(ids, executionToken)).ToDictionary(x => x.SourceLineId);
                var items = rows.OrderBy(x => x.Uid).Select(x => new InventoryPostingItem
                {
                    CommandItemId = x.Uid, WarehouseSiteId = header.WarehouseSiteId, ProductId = x.ProductId,
                    SkuSnapshot = x.SkuSnapshot, ProductNameSnapshot = x.DescriptionSnapshot, UnitSnapshot = x.UnitSnapshot,
                    QuantityDelta = x.Quantity, TransactionType = sources[x.SourceLineId].IsCustomerSupplied
                        ? InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound : InventoryTransactionTypeEnum.PurchaseReceiptInbound,
                    SourceDocumentType = "InboundDocument", SourceDocumentId = uid, SourceLineId = x.Uid,
                    SourceDocumentNoSnapshot = header.DocumentNo, OccurredDate = header.ReceiptDate
                }).ToArray();
                try { await inventoryPosting.PostAsync(command.CommandId, items, executionToken); }
                catch (InventoryPostingException ex) { throw new DraftException(ex.Message, ex.Code); }
                header.PostedDate = DateTime.Now;
                header.PostedBy = user.UserName;
                await repository.MarkPostedAsync(header, executionToken);
                var result = new InboundPostResult { Uid = uid, Version = header.Version, DocumentNo = header.DocumentNo, PostingStatus = InboundPostingStatusEnum.Posted };
                if (await logs.InsertAsync(new BusinessOperationLog
                {
                    CommandId = command.CommandId, CorrelationId = correlation.CorrelationId, Module = Module,
                    AggregateType = "InboundDocument", AggregateUid = uid, AggregateNoSnapshot = header.DocumentNo,
                    OperationType = "Post", OperationName = "入库过账", Summary = "入库状态、库存事实及余额同事务提交。",
                    ChangedFieldsJson = JsonSerializer.Serialize(new { PostingStatus = new { Before = InboundPostingStatusEnum.Unposted, After = InboundPostingStatusEnum.Posted },
                        Version = new { Before = command.ExpectedVersion, After = header.Version }, header.PostedDate, header.PostedBy }),
                    RelatedObjectsJson = JsonSerializer.Serialize(new { InboundUid = uid, LineUids = rows.Select(x => x.Uid).ToArray(),
                        SourceLineIds = ids, InventoryCommandId = command.CommandId }),
                    UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = header.PostedDate.Value
                }) != 1) throw new DraftException("过账日志写入失败，过账已回滚。");
                return result;
            }, ct);
        }, ct, false), token);

    /// <summary>在校验和回执读取前同时核对模块访问与入库过账细权限。</summary>
    private async Task<ServiceResult<T>?> AuthorizePostingAsync<T>(CancellationToken token)
    {
        var refusal = await support.AuthorizeAsync<T>(token);
        if (refusal is not null) return refusal;
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryInboundPost, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有入库过账权限。");
        return null;
    }

    private async Task RequirePostingPermissionAsync(CancellationToken token)
    {
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryInboundPost, token))
            throw new DraftException("当前账号没有入库过账权限。", ServiceErrorCodes.Forbidden);
    }

    /// <summary>预览与正式提交共用业务校验；此方法只读，不调用库存写入内核。</summary>
    private async Task<InboundPostPreviewDto> BuildPostingPreviewAsync(InboundManagerDetailDto detail, int version, CancellationToken token)
    {
        var h = detail.Header;
        var preview = new InboundPostPreviewDto { Uid = h.Uid, Version = h.Version, DocumentNo = h.DocumentNo,
            WarehouseSiteId = h.WarehouseSiteId, WarehouseName = h.WarehouseNameSnapshot };
        void Block(string message, string code = ServiceErrorCodes.BusinessBlocked) => preview.Blockers.Add(new() { Message = message, Code = code });
        if (h.Version != version) Block("入库版本已变化，请重新加载后预览。", ServiceErrorCodes.VersionConflict);
        if (h.PostingStatus != InboundPostingStatusEnum.Unposted || h.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active
            || h.CreationMode != InboundCreationModeEnum.Manual || h.TriggerPackingListId.HasValue || h.IsDeleted)
            Block("只有活动、人工、未过账且非 PL 触发的入库单可以过账。");
        if (h.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
        { Block("当前仅支持采购入库过账，其他来源类型尚未实现。"); return preview; }
        if (detail.Lines.Count is < 1 or > 500 || detail.Lines.Any(x => x.Uid == Guid.Empty || x.SourceLineId == Guid.Empty
            || x.ProductId == Guid.Empty || x.OriginalLineId.HasValue || x.Quantity <= 0 || x.Quantity > 99999999999999.9999m
            || decimal.Round(x.Quantity, 4) != x.Quantity || string.IsNullOrWhiteSpace(x.UnitSnapshot))
            || detail.Lines.Select(x => x.SourceLineId).Distinct().Count() != detail.Lines.Count)
            Block("入库明细身份、数量、单位或来源重复校验失败，请修改草稿。", ServiceErrorCodes.Validation);
        if (h.ReceiptDate == default || string.IsNullOrWhiteSpace(h.DocumentNo)) Block("入库日期或单号无效。", ServiceErrorCodes.Validation);
        if (await repository.HasLedgerAsync(h.Uid, token)) Block("该入库单已有正式库存流水，不能再次过账。");
        if (await repository.HasUnprovenReceiptAsync(token)) Block("存在无法证明采购来源的正式入库历史，请先核实。");
        var name = await repository.ReadActiveWarehouseNameAsync(h.WarehouseSiteId, token);
        if (name is null) Block("目标仓库不存在、已删除或已停用。");
        else preview.WarehouseName = name;
        var ids = detail.Lines.Select(x => x.SourceLineId).Distinct().ToArray();
        InboundPurchasePostingValidator.Validate(detail, await repository.ReadPostingSourcesAsync(ids, token), preview);
        foreach (var group in detail.Lines.GroupBy(x => x.ProductId))
        {
            var current = await repository.ReadBalanceAsync(h.WarehouseSiteId, group.Key, token);
            var delta = group.Sum(x => x.Quantity);
            preview.StockImpacts.Add(new() { ProductId = group.Key, Sku = group.First().SkuSnapshot,
                CurrentQuantity = current, QuantityDelta = delta, ExpectedQuantity = current + delta });
            if (current + delta > 99999999999999.9999m || current + delta < 0) Block("预计库存余额超出允许范围。");
        }
        preview.CanPost = preview.Blockers.Count == 0;
        return preview;
    }
}
