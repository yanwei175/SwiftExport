using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>入库单据编排：草稿不动库存，库存写入只经 IInventoryPostingService。</summary>
public sealed class InboundWorkspaceService(ServiceBaseContext context, IInboundDocumentRepository documents, IInboundSourceRepository sources, IInboundQueryRepository queries,
    IInventoryPostingService posting, IBusinessOperationLogRepository operationLogs, ICorrelationContext correlation, ICurrentUser currentUser) : IInboundWorkspaceService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<InboundWorkspaceService> _logger = context.LoggerFactory.CreateLogger<InboundWorkspaceService>();

    public async Task<ServiceResult<PagedResponseBase<InboundListItemDto>>> SearchAsync(InboundSearchRequest request, CancellationToken cancellationToken = default) => await ReadAsync(() => documents.SearchAsync(request ?? new InboundSearchRequest(), cancellationToken), "查询入库工作台失败。");
    public async Task<ServiceResult<PagedResponseBase<InboundSourceDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken cancellationToken = default) => await ReadAsync(() => sources.SearchAsync(request ?? new InboundSourceSearchRequest(), cancellationToken), "查询可入库来源失败。");
    public async Task<ServiceResult<InboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default) => await ReadAsync(async () =>
    {
        var header = await documents.GetHeaderAsync(uid, cancellationToken) ?? throw new InboundException("入库单不存在。");
        return new InboundDetailDto { Header = header, Lines = (await documents.GetLinesAsync(uid, cancellationToken)).ToList(), Ledgers = (await documents.GetLedgersAsync(uid, cancellationToken)).ToList() };
    }, "加载入库单失败。");
    public async Task<ServiceResult<InboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default) => await ReadAsync(async () => await queries.GetTraceAsync(uid, cancellationToken) ?? throw new InboundException("入库单不存在。"), "加载入库追溯失败。");

    public Task<ServiceResult<InboundCommandResultDto>> CreateDraftAsync(CreateInboundDraftCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.CommandId == Guid.Empty || command.DocumentType == InboundDocumentTypeEnum.OpeningBalance || !ValidLines(command.Lines)) return Task.FromResult(ServiceResult<InboundCommandResultDto>.Fail("入库类型、命令标识和明细不能为空。"));
        return ExecuteAsync(async () =>
        {
            var existing = await documents.GetByCommandForUpdateAsync(command.CommandId);
            if (existing is not null) return Result(existing, true, "入库草稿已存在。");
            var built = await BuildLinesAsync(command.DocumentType, command.Lines);
            var first = built.Sources[0];
            if (built.Sources.Any(x => x.WarehouseSiteId != first.WarehouseSiteId)) throw new InboundException("同一入库单的所有明细必须属于同一目标虚拟仓。");
            var document = new InboundDocument { Uid = GuidSequentialGenerator.NewGuid(), DocumentNo = $"IR{DateTime.Now:yyyyMMddHHmmssfff}", DocumentType = command.DocumentType, LifecycleStatus = InventoryDocumentLifecycleStatusEnum.Active, PostingStatus = InboundPostingStatusEnum.Unposted, WarehouseSiteId = first.WarehouseSiteId, WarehouseNameSnapshot = first.WarehouseName, SupplierId = first.SupplierId, SupplierNameSnapshot = first.SupplierName, ReceiptDate = command.ReceiptDate == default ? DateTime.Today : command.ReceiptDate.Date, Remark = Normalize(command.Remark, 2000), CommandId = command.CommandId };
            if (await documents.InsertAsync(document, built.Lines) != built.Lines.Count + 1) throw new InboundException("入库草稿保存失败。");
            return Result(document, false, "入库草稿已保存。");
        }, cancellationToken);
    }

    public Task<ServiceResult<InboundCommandResultDto>> UpdateDraftAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || !ValidLines(command.Lines)) return Task.FromResult(ServiceResult<InboundCommandResultDto>.Fail("入库草稿、版本或明细无效。"));
        return ExecuteAsync(async () =>
        {
            var document = await RequireDraftAsync(uid, command.ExpectedVersion); var built = await BuildLinesAsync(document.DocumentType, command.Lines);
            if (built.Sources.Any(x => x.WarehouseSiteId != document.WarehouseSiteId)) throw new InboundException("草稿不能变更目标虚拟仓。");
            document.ReceiptDate = command.ReceiptDate == default ? document.ReceiptDate : command.ReceiptDate.Date; document.Remark = Normalize(command.Remark, 2000);
            if (await documents.UpdateDraftAsync(document, built.Lines, command.ExpectedVersion) <= 0) throw new InboundException("草稿保存发生并发冲突，请刷新后重试。");
            document.Version++; return Result(document, false, "入库草稿已更新。");
        }, cancellationToken);
    }

    public Task<ServiceResult<InboundPostPreviewDto>> PostPreviewAsync(Guid uid, InboundPostCommand command, CancellationToken cancellationToken = default) => PreviewAsync(uid, command?.ExpectedVersion ?? 0, false, cancellationToken);
    public Task<ServiceResult<InboundPostPreviewDto>> ReversePreviewAsync(Guid uid, InboundReverseCommand command, CancellationToken cancellationToken = default) => PreviewAsync(uid, command?.ExpectedVersion ?? 0, true, cancellationToken);

    public Task<ServiceResult<InboundCommandResultDto>> PostAsync(Guid uid, InboundPostCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.PreviewToken)) return Task.FromResult(ServiceResult<InboundCommandResultDto>.Fail("过账命令、版本和 PreviewToken 不能为空。"));
        return ExecuteAsync(async () =>
        {
            var applied = await documents.GetByCommandForUpdateAsync(command.CommandId);
            if (applied is not null)
            {
                if (applied.Uid != uid || applied.PostingStatus != InboundPostingStatusEnum.Posted) throw new InboundException("IDEMPOTENCY_CONFLICT：CommandId 已用于不同入库事实。");
                return Result(applied, true, "入库已过账。");
            }
            var document = await RequireDraftAsync(uid, command.ExpectedVersion); var lines = await documents.GetLinesForUpdateAsync(uid);
            if (lines.Count == 0) throw new InboundException("入库单没有明细。");
            if (!string.Equals(command.PreviewToken, Token(uid, document.Version, false, lines), StringComparison.OrdinalIgnoreCase)) throw new InboundException("入库预览已失效，请重新预览。");
            foreach (var line in lines)
            {
                var source = await sources.GetForUpdateAsync(line.SourceType, line.SourceUid) ?? throw new InboundException("入库来源已失效。");
                var received = await sources.GetReceivedNetQtyAsync(line.SourceType, line.SourceUid, uid);
                if (line.Quantity <= 0 || line.Quantity > source.EffectiveQty - received) throw new InboundException($"{line.SkuSnapshot} 的可入库数量已变化，请刷新后重试。");
                if (source.WarehouseSiteId != document.WarehouseSiteId) throw new InboundException("来源的目标虚拟仓已变化。");
            }
            var expected = command.Lines?.ToDictionary(x => x.InboundLineId) ?? new Dictionary<Guid, InboundPostingLineExpectation>();
            if (expected.Count > 0 && lines.Any(x => !expected.TryGetValue(x.Uid, out var item) || item.ExpectedVersion != x.Version)) throw new InboundException("入库明细已变化，请刷新后重试。");
            var items = lines.Select(x => new InventoryPostingItem { CommandItemId = x.Uid, WarehouseSiteId = document.WarehouseSiteId, ProductId = x.ProductId, QuantityDelta = x.Quantity, TransactionType = ToInboundTransaction(document.DocumentType), SourceDocumentType = "InboundDocument", SourceDocumentId = document.Uid, SourceLineId = x.Uid, OccurredDate = document.ReceiptDate }).ToList();
            await posting.PostAsync(command.CommandId, items, cancellationToken);
            if (await documents.SetPostedAsync(uid, command.ExpectedVersion, DateTime.Now, currentUser.UserName, command.CommandId) != 1) throw new InboundException("入库过账发生并发冲突，请刷新后重试。");
            document.PostingStatus = InboundPostingStatusEnum.Posted; document.Version++;
            await WriteLogAsync(command.CommandId, document, "Inbound.Posted", "入库单已过账", null);
            return Result(document, false, "入库已过账。");
        }, cancellationToken);
    }

    public Task<ServiceResult<InboundCommandResultDto>> ReverseAsync(Guid uid, InboundReverseCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.PreviewToken) || string.IsNullOrWhiteSpace(command.Reason)) return Task.FromResult(ServiceResult<InboundCommandResultDto>.Fail("反过账命令、版本、PreviewToken 和原因不能为空。"));
        return ExecuteAsync(async () =>
        {
            var applied = await documents.GetByCommandForUpdateAsync(command.CommandId);
            if (applied is not null)
            {
                if (applied.Uid != uid || applied.PostingStatus != InboundPostingStatusEnum.Reversed) throw new InboundException("IDEMPOTENCY_CONFLICT：CommandId 已用于不同入库事实。");
                return Result(applied, true, "入库已反过账。");
            }
            var document = await documents.GetForUpdateAsync(uid) ?? throw new InboundException("入库单不存在。");
            if (document.Version != command.ExpectedVersion || document.PostingStatus != InboundPostingStatusEnum.Posted) throw new InboundException("入库单状态或版本不允许反过账。");
            if (await sources.HasDownstreamOutboundAsync(uid)) throw new InboundException("存在后续已过账出库，请先反向后续单据。");
            var ledgers = await documents.GetLedgersAsync(uid, cancellationToken); var lines = await documents.GetLinesForUpdateAsync(uid);
            if (!string.Equals(command.PreviewToken, Token(uid, document.Version, true, lines), StringComparison.OrdinalIgnoreCase)) throw new InboundException("入库反过账预览已失效，请重新预览。");
            var items = ledgers.Where(x => x.QuantityDelta > 0).Select(x => new InventoryPostingItem { CommandItemId = GuidSequentialGenerator.NewGuid(), WarehouseSiteId = x.WarehouseSiteId, ProductId = x.ProductId, QuantityDelta = -x.QuantityDelta, TransactionType = InventoryTransactionTypeEnum.Reversal, SourceDocumentType = "InboundDocumentReverse", SourceDocumentId = uid, SourceLineId = x.SourceLineId, ReversalOfLedgerId = x.Uid, Reason = command.Reason, OccurredDate = DateTime.Now }).ToList();
            if (items.Count != lines.Count) throw new InboundException("原始库存流水不完整，不能反过账。");
            await posting.PostAsync(command.CommandId, items, cancellationToken);
            if (await documents.SetReversedAsync(uid, command.ExpectedVersion, DateTime.Now, currentUser.UserName, Normalize(command.Reason, 1000)!, command.CommandId) != 1) throw new InboundException("反过账发生并发冲突，请刷新后重试。");
            document.PostingStatus = InboundPostingStatusEnum.Reversed; document.Version++;
            await WriteLogAsync(command.CommandId, document, "Inbound.Reversed", "入库单已反过账", command.Reason);
            return Result(document, false, "入库已反过账。");
        }, cancellationToken);
    }

    private async Task<ServiceResult<InboundPostPreviewDto>> PreviewAsync(Guid uid, int expectedVersion, bool reverse, CancellationToken cancellationToken) => await ReadAsync(async () =>
    {
        var document = await documents.GetHeaderAsync(uid, cancellationToken) ?? throw new InboundException("入库单不存在。");
        if (document.Version != expectedVersion) throw new InboundException("入库单已变化，请刷新后重试。");
        var lines = await documents.GetLinesAsync(uid, cancellationToken); var preview = new InboundPostPreviewDto { InboundDocumentId = uid, Version = document.Version };
        if (reverse) preview.Blockers.Add((await sources.HasDownstreamOutboundAsync(uid)) ? "存在后续已过账出库。" : string.Empty);
        foreach (var line in lines) { var source = await sources.GetForUpdateAsync(line.SourceType, line.SourceUid); if (source is null) preview.Blockers.Add($"{line.Sku} 来源失效。"); else preview.Lines.Add(new InboundPostPreviewLineDto { InboundLineId = line.Uid, Quantity = line.Quantity, RemainingReceivableQty = source.RemainingReceivableQty, BalanceBefore = 0, BalanceAfter = line.Quantity }); }
        preview.Blockers = preview.Blockers.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(); preview.HasBlockers = preview.Blockers.Count > 0; preview.PreviewToken = Token(uid, document.Version, reverse, lines); return preview;
    }, "生成入库预览失败。");

    private async Task<(List<InboundDocumentLine> Lines, List<InboundSourceDto> Sources)> BuildLinesAsync(InboundDocumentTypeEnum type, IReadOnlyList<InboundDraftLineCommand> commands)
    {
        var lines = new List<InboundDocumentLine>(); var sourceList = new List<InboundSourceDto>(); var no = 1;
        foreach (var command in commands)
        {
            var sourceType = type == InboundDocumentTypeEnum.CustomerSuppliedReceipt ? InboundSourceTypeEnum.NetDemandAllocation : InboundSourceTypeEnum.PurchaseOrderLine;
            var source = await sources.GetForUpdateAsync(sourceType, command.SourceUid) ?? throw new InboundException("入库来源不存在或已失效。");
            if (source.SourceVersion != command.ExpectedSourceVersion || command.Quantity <= 0 || command.Quantity > command.ExpectedRemainingQty || command.Quantity > source.RemainingReceivableQty) throw new InboundException("入库数量超过可收货剩余量，或来源已变化。");
            lines.Add(new InboundDocumentLine { Uid = command.InboundLineId ?? GuidSequentialGenerator.NewGuid(), Version = command.ExpectedVersion ?? 1, LineNumber = no++, ProductId = source.ProductId, SkuSnapshot = source.Sku, DescriptionSnapshot = source.Description, UnitSnapshot = source.Unit, Quantity = command.Quantity, SourceType = sourceType, SourceUid = source.SourceUid, DemandExecutionLinkId = source.DemandExecutionLinkId, NetDemandAllocationId = source.NetDemandAllocationId, SalesOrderNoSnapshot = source.SalesOrderNo }); sourceList.Add(source);
        }
        return (lines, sourceList);
    }
    private async Task<InboundDocument> RequireDraftAsync(Guid uid, int version) { var document = await documents.GetForUpdateAsync(uid) ?? throw new InboundException("入库单不存在。"); if (document.Version != version || document.PostingStatus != InboundPostingStatusEnum.Unposted) throw new InboundException("入库单不是可编辑草稿或版本已变化。"); return document; }
    private async Task<ServiceResult<T>> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken) { if (_session.Transaction is not null) return ServiceResult<T>.Fail("当前请求已存在未完成事务。"); _session.Transaction = _session.Connection.BeginTransaction(); try { var result = await action(); cancellationToken.ThrowIfCancellationRequested(); _session.Transaction.Commit(); return ServiceResult<T>.Ok(result); } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _session.Transaction.Rollback(); throw; } catch (InboundException ex) { _session.Transaction.Rollback(); return ServiceResult<T>.Fail(ex.Message); } catch (Exception ex) { _session.Transaction.Rollback(); _logger.LogError(ex, "入库命令执行失败。"); return ServiceResult<T>.Fail("入库命令执行失败。"); } finally { _session.Transaction.Dispose(); _session.Transaction = null; } }
    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action, string message) { try { return ServiceResult<T>.Ok(await action()); } catch (InboundException ex) { return ServiceResult<T>.Fail(ex.Message); } catch (Exception ex) { _logger.LogError(ex, message); return ServiceResult<T>.Fail(message); } }
    private static bool ValidLines(IReadOnlyCollection<InboundDraftLineCommand>? lines) => lines is { Count: > 0 } && lines.All(x => x.SourceUid != Guid.Empty && x.ExpectedSourceVersion > 0 && x.ExpectedRemainingQty > 0 && x.Quantity > 0);
    private static InventoryTransactionTypeEnum ToInboundTransaction(InboundDocumentTypeEnum type) => type switch { InboundDocumentTypeEnum.PurchaseReceipt => InventoryTransactionTypeEnum.PurchaseReceiptInbound, InboundDocumentTypeEnum.CustomerSuppliedReceipt => InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound, InboundDocumentTypeEnum.PackingListAutoReceipt => InventoryTransactionTypeEnum.PackingListAutoReceiptInbound, _ => throw new InboundException("不支持的入库类型。") };
    private static string Token(Guid uid, int version, bool reverse, IReadOnlyList<InboundLineDto> lines) { var text = $"{uid:N}|{version}|{reverse}|" + string.Join(";", lines.OrderBy(x => x.Uid).Select(x => $"{x.Uid:N}:{x.Version}:{x.Quantity}")); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))); }
    private static string Token(Guid uid, int version, bool reverse, IReadOnlyList<InboundDocumentLine> lines) { var text = $"{uid:N}|{version}|{reverse}|" + string.Join(";", lines.OrderBy(x => x.Uid).Select(x => $"{x.Uid:N}:{x.Version}:{x.Quantity}")); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))); }
    private async Task WriteLogAsync(Guid commandId, InboundDocument document, string operationType, string operationName, string? reason)
    {
        if (await operationLogs.GetByCommandAsync("Inventory", operationType, commandId) is not null) return;
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            CorrelationId = Normalize(correlation.CorrelationId, 64) ?? commandId.ToString("N"),
            CommandId = commandId,
            Module = "Inventory",
            AggregateType = "InboundDocument",
            AggregateUid = document.Uid,
            AggregateNoSnapshot = document.DocumentNo,
            OperationType = operationType,
            OperationName = operationName,
            Reason = Normalize(reason, 1000),
            Summary = operationName,
            UserUid = currentUser.UserId,
            UserNameSnapshot = currentUser.UserName,
            OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1) throw new InboundException("业务操作日志写入失败。");
    }
    private static InboundCommandResultDto Result(InboundDocument document, bool alreadyApplied, string message) => new() { InboundDocumentId = document.Uid, Version = document.Version, PostingStatus = document.PostingStatus, AlreadyApplied = alreadyApplied, Message = message };
    private static string? Normalize(string? value, int max) { var text = value?.Trim(); return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, max)]; }
    private sealed class InboundException(string message) : Exception(message);
}
