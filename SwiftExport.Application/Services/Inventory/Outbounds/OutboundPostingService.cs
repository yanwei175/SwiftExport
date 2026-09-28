using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
namespace SwiftExport.Application.Services.Inventory.Outbounds;

/// <summary>正向出库过账编排；普通库存出库不消费需求或采购来源，库存内核只关联出库凭证。</summary>
public sealed class OutboundPostingService(IOutboundQueryRepository queries, IOutboundDocumentRepository repository,
    IOutboundExecutionReadRepository executionReads, IBusinessOperationLogRepository logs,
    ICurrentUser user, IPermissionEvaluationService permissions, IInventoryPostingService inventoryPosting,
    BusinessCommandExecutor executor, ICorrelationContext correlation, CommandPipeline pipeline,
    OutboundServiceSupport support) : IOutboundPostingService
{
    /// <inheritdoc/>
    public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            await RequirePostingPermissionAsync(token);
            if (uid == Guid.Empty || request is null || request.ExpectedVersion <= 0)
                throw new OutboundDraftException("单据身份和版本无效。", ServiceErrorCodes.Validation);
            var detail = await queries.GetDetailAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在或已删除。");
            return await BuildPostingPreviewAsync(detail, request.ExpectedVersion, token);
        }, token);

    /// <summary>授权和验证后交给公共执行器，成功重放不重复写入库存或业务审计。</summary>
    public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token = default) =>
        pipeline.ExecuteAsync<PostOutboundCommand, OutboundPostResult>(command, command?.CommandId,
            "Inventory.Outbound.Post", AuthorizePostingAsync<OutboundPostResult>,
            ct => ExecutePostCommandAsync(uid, command!, ct), token);

    /// <summary>校验路由身份，并由执行器持有命令锁、事务和持久化回执。</summary>
    private Task<ServiceResult<CommandExecutionResult<OutboundPostResult>>> ExecutePostCommandAsync(
        Guid uid, PostOutboundCommand command, CancellationToken token) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new OutboundDraftException("单据标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await executor.ExecuteAsync(command.CommandId, "Inventory.Outbound.Post", uid,
                new { command.ExpectedVersion }, ct => PostInTransactionAsync(uid, command, ct), token);
        }, token, checkPermission: false);

    /// <summary>在执行器事务内依次锁定、重验、写库存、更新单据并记录业务审计。</summary>
    private async Task<OutboundPostResult> PostInTransactionAsync(Guid uid, PostOutboundCommand command, CancellationToken token)
    {
        var header = await repository.LockHeaderAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在或已删除。");
        if (header.Version != command.ExpectedVersion)
            throw new OutboundDraftException("出库单已被其他人修改，请刷新后重试。", ServiceErrorCodes.VersionConflict, "ExpectedVersion");
        if (header.DocumentType != OutboundDocumentTypeEnum.StockIssue || header.PostingStatus != OutboundPostingStatusEnum.Unposted
            || header.LifecycleStatus != InventoryDocumentLifecycleStatusEnum.Active || header.TriggerPackingListId.HasValue)
            throw new OutboundDraftException("只允许活动、未过账的人工草稿过账。");
        if (await repository.HasLedgerAsync(uid, token))
            throw new OutboundDraftException("该单存在正式库存流水，禁止再次过账。");
        var rows = await repository.LockLinesAsync(uid, token);
        var detail = await queries.GetDetailAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在。");
        var preview = await BuildPostingPreviewAsync(detail, command.ExpectedVersion, token);
        if (!preview.CanPost)
        {
            var first = preview.Blockers[0];
            throw new OutboundDraftException(first.Message, first.Code, first.LineId.HasValue ? $"Lines[{first.LineId}].Quantity" : null);
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
        catch (InventoryPostingException ex) { throw new OutboundDraftException(ex.Message, ex.Code); }

        header.PostedDate = DateTime.Now;
        header.PostedBy = user.UserName;
        await repository.MarkPostedAsync(header, token);
        var result = new OutboundPostResult { Uid = uid, Version = header.Version, DocumentNo = header.DocumentNo, PostingStatus = OutboundPostingStatusEnum.Posted };
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            CommandId = command.CommandId, CorrelationId = correlation.CorrelationId, Module = "OutboundManager",
            AggregateType = "OutboundDocument", AggregateUid = uid, AggregateNoSnapshot = header.DocumentNo,
            OperationType = "Post", OperationName = "出库过账", Summary = "出库状态、库存事实及余额同事务提交。",
            Reason = header.IssueReason,
            ChangedFieldsJson = JsonSerializer.Serialize(new { PostingStatus = new { Before = OutboundPostingStatusEnum.Unposted, After = OutboundPostingStatusEnum.Posted }, command.ExpectedVersion, header.Version }),
            RelatedObjectsJson = JsonSerializer.Serialize(new { result.Uid, result.DocumentNo, result.Version, LineIds = rows.Select(x => x.Uid).ToArray() }),
            UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = header.PostedDate.Value
        }) != 1) throw new OutboundDraftException("过账日志写入失败，过账已回滚。");
        return result;
    }

    /// <summary>在请求验证和回执读取前检查库存模块与出库过账权限。</summary>
    private async Task<ServiceResult<T>?> AuthorizePostingAsync<T>(CancellationToken token)
    {
        var refusal = await support.AuthorizeAsync<T>(token, PermissionKeys.InventoryOutboundPost);
        if (refusal is not null) return refusal;
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryOutboundPost, token))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有出库过账权限。");
        return null;
    }
    private async Task RequirePostingPermissionAsync(CancellationToken token)
    {
        if (!await permissions.HasPermissionAsync(user.UserId!.Value, PermissionKeys.InventoryOutboundPost, token))
            throw new OutboundDraftException("当前账号没有出库过账权限。", ServiceErrorCodes.Forbidden);
    }

    /// <summary>预览不预留库存；正式提交再次检查，最终负库存门禁由统一库存锁保护。</summary>
    private async Task<OutboundPostPreviewDto> BuildPostingPreviewAsync(OutboundManagerDetailDto detail, int version, CancellationToken token)
    {
        var h = detail.Header;
        var preview = new OutboundPostPreviewDto { Uid = h.Uid, Version = h.Version, DocumentNo = h.DocumentNo,
            WarehouseSiteId = h.WarehouseSiteId, WarehouseName = h.WarehouseNameSnapshot, IssueReason = h.IssueReason };
        void Block(string message, string code = ServiceErrorCodes.BusinessBlocked) => preview.Blockers.Add(new() { Message = message, Code = code });
        // 草稿不强制用途，但正式库存减少必须有原因；历史空值只读保留，不猜测回填。
        if (string.IsNullOrWhiteSpace(h.IssueReason) || h.IssueReason.Length > 500)
            Block("请填写出库原因（最多 500 字），保存后再过账。", ServiceErrorCodes.Validation);
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
        var name = await executionReads.ReadActiveWarehouseNameAsync(h.WarehouseSiteId, token);
        if (name is null) Block("仓库不存在、已删除或已停用。"); else preview.WarehouseName = name;
        var products = (await queries.GetProductsAsync(detail.Lines.Select(x => x.ProductId).ToArray(), token)).ToDictionary(x => x.ProductId);
        foreach (var line in detail.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || string.IsNullOrWhiteSpace(product.Unit)
                || !string.Equals(line.UnitSnapshot.Trim(), product.Unit.Trim(), StringComparison.OrdinalIgnoreCase))
                Block($"产品 {line.SkuSnapshot} 已删除或单位与快照不一致。");
            var current = await executionReads.ReadBalanceAsync(h.WarehouseSiteId, line.ProductId, token);
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
