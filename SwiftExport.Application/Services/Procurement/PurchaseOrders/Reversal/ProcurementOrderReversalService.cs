using System.Data;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Reversal;

/// <summary>采购反向权威编排；预览保持一致读取，写命令以公共回执和可串行化事务执行。</summary>
public sealed class ProcurementOrderReversalService(
    ProcurementOrderReadSupport reads, IProcurementReversalRepository reversals,
    IBusinessOperationLogRepository logs, ICurrentUser user, ICorrelationContext correlation,
    BusinessCommandExecutor executor, CommandPipeline pipeline, ProcurementOrderCommandSupport support)
    : IProcurementOrderReversalService
{
    private const string Module = "Procurement.OrderManagement";

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> DeleteDraftAsync(Guid uid,
        DeleteProcurementDraftCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<DeleteProcurementDraftCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            CommandType(ProcurementReversalOperation.DeleteDraft),
            token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Edit", token),
            token => ExecuteReversalCommandAsync(uid, ProcurementReversalOperation.DeleteDraft,
                command!.CommandId, command.ExpectedVersion, command.Reason, command.Lines, token), ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewAsync(Guid uid, ProcurementReversalOperation operation,
        ProcurementReversalPreviewRequest request, CancellationToken ct = default)
        => reads.ReadConsistentAsync(Permission(operation), async () =>
        {
            Validate(uid, request?.ExpectedVersion ?? 0);
            return ProcurementReversalPolicy.Build(ProcurementOrderCommandSupport.RequireLockedFacts(await reversals.LockAsync(uid, ct)), operation, request!);
        }, ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid,
        CancelProcurementOrderConfirmationCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<CancelProcurementOrderConfirmationCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            CommandType(ProcurementReversalOperation.CancelConfirmation),
            token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Confirm", token),
            token => ExecuteReversalCommandAsync(uid, ProcurementReversalOperation.CancelConfirmation,
                command!.CommandId, command.ExpectedVersion, command.Reason, [], token), ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> VoidAsync(Guid uid,
        VoidProcurementOrderCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<VoidProcurementOrderCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            CommandType(ProcurementReversalOperation.Void),
            token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Void", token),
            token => ExecuteReversalCommandAsync(uid, ProcurementReversalOperation.Void,
                command!.CommandId, command.ExpectedVersion, command.Reason, [], token), ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> DeleteLinesAsync(Guid uid,
        DeleteProcurementDraftLinesCommand command, CancellationToken ct = default) =>
        pipeline.ExecuteAsync<DeleteProcurementDraftLinesCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            CommandType(ProcurementReversalOperation.DeleteLines),
            token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Edit", token),
            token => ExecuteReversalCommandAsync(uid, ProcurementReversalOperation.DeleteLines,
                command!.CommandId, command.ExpectedVersion, command.Reason, command.Lines, token), ct);

    /// <summary>规范化原因和行顺序后校验路由身份；公共执行器先锁命令再读取来源事实。</summary>
    private Task<ServiceResult<CommandExecutionResult<ProcurementOrderCommandResult>>> ExecuteReversalCommandAsync(
        Guid uid, ProcurementReversalOperation operation, Guid commandId, int version, string? reason,
        List<ProcurementLineVersionDto>? lines, CancellationToken ct) => support.RunAsync(async () =>
        {
            Validate(uid, version);
            reason = reason?.Trim();
            var orderedLines = lines!.OrderBy(x => x.PurchaseOrderLineId).ToList();
            return await executor.ExecuteAsync(commandId, CommandType(operation), uid,
                new { ExpectedVersion = version, Reason = reason, Lines = orderedLines },
                token => ReversalInTransactionAsync(uid, operation, commandId, version, reason!, orderedLines, token),
                ct, IsolationLevel.Serializable);
        }, ct);

    /// <summary>仅首次执行时依原锁序核验下游、应用状态及引用释放，并同事务写入业务审计。</summary>
    private async Task<ProcurementOrderCommandResult> ReversalInTransactionAsync(Guid uid, ProcurementReversalOperation operation,
        Guid commandId, int version, string reason, List<ProcurementLineVersionDto> lines, CancellationToken ct)
    {
        var facts = ProcurementOrderCommandSupport.RequireLockedFacts(await reversals.LockAsync(uid, ct));
        var request = new ProcurementReversalPreviewRequest { ExpectedVersion = version, Lines = lines };
        var preview = ProcurementReversalPolicy.Build(facts, operation, request);
        if (!preview.CanExecute) throw ProcurementOperationException.Blocked(string.Join("\n", preview.Blockers));
        // 保存操作前快照，避免后续实体变更覆盖恢复资格所需的版本及原始引用。
        var before = JsonSerializer.SerializeToElement(new { facts.Head, facts.Lines, facts.Sources, facts.Demands, facts.Links });
        ProcurementOrderCommandSupport.EnsureWriteSucceeded(await reversals.ApplyAsync(facts, operation, preview.Lines.Select(x => x.PurchaseOrderLineId).ToArray(), reason, ct));
        var head = facts.Head!;
        var operationName = operation.ToString();
        var result = new ProcurementOrderCommandResult { PurchaseOrderId = uid, OrderNo = head.OrderNo, Version = head.Version,
            IsDeleted=head.IsDeleted,
            ConfirmationStatus = head.ConfirmationStatus, LifecycleStatus = head.LifecycleStatus, ConfirmationId = head.ActiveConfirmationId,
            Lines = facts.Lines.Where(x => operation==ProcurementReversalOperation.DeleteDraft
                ? lines.Any(input=>input.PurchaseOrderLineId==x.Uid) : !x.IsDeleted)
                .Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList() };
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            CommandId = commandId, CorrelationId = correlation.CorrelationId, Module = Module,
            AggregateType = "PurchaseOrder", AggregateUid = uid, AggregateNoSnapshot = head.OrderNo,
            OperationType = operationName, OperationName = Title(operation), Reason = reason,
            Summary = operation == ProcurementReversalOperation.CancelConfirmation ? "采购回到草稿，保留需求承接。" : "采购反向完成；仅解除本次选定行的活动引用。",
            ChangedFieldsJson = JsonSerializer.Serialize(new { Before = before, After = new { facts.Head, facts.Lines, facts.Links }, Released = preview.Contributions }),
            RelatedObjectsJson = JsonSerializer.Serialize(result), UserUid = user.UserId,
            UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
        }) != 1) throw new InvalidOperationException("采购反向日志写入失败，全部操作已回滚。");
        return result;
    }

    /// <summary>路由单据身份和正版本不进入回执锁，防止无效目标占用命令。</summary>
    private static void Validate(Guid uid, int version)
    {
        if (uid == Guid.Empty || version <= 0) throw ProcurementOperationException.Validation("采购身份和版本无效。", "ExpectedVersion");
    }

    /// <summary>反向操作各自使用独立稳定的公共回执类型。</summary>
    private static string CommandType(ProcurementReversalOperation operation) => operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => "Procurement.Order.Reversal.CancelConfirmation",
        ProcurementReversalOperation.DeleteLines => "Procurement.Order.Reversal.DeleteLines",
        ProcurementReversalOperation.DeleteDraft => "Procurement.Order.Reversal.DeleteDraft",
        ProcurementReversalOperation.Void => "Procurement.Order.Reversal.Void",
        _ => throw new InvalidOperationException("未知采购反向操作。")
    };

    /// <summary>沿用原三种反向操作的细权限；预览同样使用此映射。</summary>
    private static string Permission(ProcurementReversalOperation operation) => operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => "Confirm", ProcurementReversalOperation.DeleteLines => "Edit",
        ProcurementReversalOperation.DeleteDraft => "Edit",
        ProcurementReversalOperation.Void => "Void", _ => throw new InvalidOperationException("未知采购反向操作。")
    };

    /// <summary>保留既有业务审计操作名称。</summary>
    private static string Title(ProcurementReversalOperation operation) => operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => "取消采购确认",
        ProcurementReversalOperation.DeleteDraft => "整单删除采购草稿",
        ProcurementReversalOperation.DeleteLines => "删除采购草稿行", _ => "作废采购单"
    };
}
