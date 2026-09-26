using System.Text.Json;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;

namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>采购反向权威编排；权限、幂等、资格重验、状态、引用与审计保持事务一致。</summary>
public sealed class ProcurementOrderReversalService(ProcurementOrderTransaction transaction,
    IProcurementReversalRepository reversals, IProcurementOrderRepository orders,
    IBusinessOperationLogRepository logs, ICurrentUser user) : IProcurementOrderReversalService
{
    private const string Module = "Procurement.OrderManagement";

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewAsync(Guid uid, ProcurementReversalOperation operation, ProcurementReversalPreviewRequest request, CancellationToken ct = default)
        => transaction.ExecuteAsync(Permission(operation), async () =>
        {
            Validate(uid, request?.ExpectedVersion ?? 0);
            return ProcurementReversalPolicy.Build(await reversals.LockAsync(uid, ct), operation, request!);
        }, ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, CancelProcurementOrderConfirmationCommand command, CancellationToken ct = default)
        => ExecuteAsync(uid, ProcurementReversalOperation.CancelConfirmation, command?.CommandId ?? Guid.Empty,
            command?.ExpectedVersion ?? 0, command?.Reason, [], ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> VoidAsync(Guid uid, VoidProcurementOrderCommand command, CancellationToken ct = default)
        => ExecuteAsync(uid, ProcurementReversalOperation.Void, command?.CommandId ?? Guid.Empty,
            command?.ExpectedVersion ?? 0, command?.Reason, [], ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> DeleteLinesAsync(Guid uid, DeleteProcurementDraftLinesCommand command, CancellationToken ct = default)
        => ExecuteAsync(uid, ProcurementReversalOperation.DeleteLines, command?.CommandId ?? Guid.Empty,
            command?.ExpectedVersion ?? 0, command?.Reason, command?.Lines, ct);

    /// <summary>先读取原回执，再锁定来源事实；重放不会被当前版本或已删行阻断，也不会重新释放。</summary>
    private Task<ServiceResult<ProcurementOrderCommandResult>> ExecuteAsync(Guid uid, ProcurementReversalOperation operation,
        Guid commandId, int version, string? reason, List<ProcurementLineVersionDto>? lines, CancellationToken ct)
        => transaction.ExecuteAsync(Permission(operation), async () =>
        {
            Validate(uid, version);
            reason = reason?.Trim();
            if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || lines is null || lines.Any(x => x is null))
                throw ProcurementOperationException.Validation("命令标识、行集合及 1–500 字原因必须有效。", "Reason");
            var operationName = operation.ToString();
            var request = new ProcurementReversalPreviewRequest { ExpectedVersion = version, Lines = lines.OrderBy(x => x.PurchaseOrderLineId).ToList() };
            var fingerprint = ProcurementOrderPolicy.Fingerprint(new { uid, operation = operationName, commandId, request, reason });
            var prior = await orders.GetReceiptAsync(commandId, ct);
            if (prior is not null)
            {
                if (prior.ActorUid != user.UserId || prior.Operation != operationName || prior.PurchaseOrderId != uid || prior.RequestFingerprint != fingerprint)
                    throw new ProcurementOperationException(ServiceErrorCodes.CommandConflict, "CommandId 已用于其他用户、操作或内容。", "CommandId");
                return JsonSerializer.Deserialize<ProcurementOrderCommandResult>(prior.ResultJson)
                    ?? throw new InvalidOperationException("原始命令回执不完整，不能安全重放。");
            }
            var facts = await reversals.LockAsync(uid, ct);
            var preview = ProcurementReversalPolicy.Build(facts, operation, request);
            if (!preview.CanExecute) throw ProcurementOperationException.Blocked(string.Join("\n", preview.Blockers));
            // 保存操作前快照，避免后续实体变更覆盖恢复资格所需的版本及原始引用。
            var before = JsonSerializer.SerializeToElement(new { facts.Head, facts.Lines, facts.Sources, facts.Demands, facts.Links });
            await reversals.ApplyAsync(facts, operation, preview.Lines.Select(x => x.PurchaseOrderLineId).ToArray(), reason, ct);
            var head = facts.Head!;
            var result = new ProcurementOrderCommandResult { PurchaseOrderId = uid, OrderNo = head.OrderNo, Version = head.Version,
                ConfirmationStatus = head.ConfirmationStatus, LifecycleStatus = head.LifecycleStatus, ConfirmationId = head.ActiveConfirmationId,
                Lines = facts.Lines.Where(x => !x.IsDeleted).Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList() };
            if (await logs.InsertAsync(new BusinessOperationLog
            {
                CommandId = commandId, CorrelationId = commandId.ToString("N"), Module = Module,
                AggregateType = "PurchaseOrder", AggregateUid = uid, AggregateNoSnapshot = head.OrderNo,
                OperationType = operationName, OperationName = Title(operation), Reason = reason,
                Summary = operation == ProcurementReversalOperation.CancelConfirmation ? "采购回到草稿，保留需求承接。" : "采购反向完成；仅解除本次选定行的活动引用。",
                ChangedFieldsJson = JsonSerializer.Serialize(new { Before = before, After = new { facts.Head, facts.Lines, facts.Links }, Released = preview.Contributions }),
                RelatedObjectsJson = JsonSerializer.Serialize(result), UserUid = user.UserId,
                UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
            }) != 1) throw new InvalidOperationException("采购反向日志写入失败，全部操作已回滚。");
            await orders.InsertReceiptAsync(new ProcurementCommandReceipt { CommandId = commandId, ActorUid = user.UserId!.Value,
                PurchaseOrderId = uid, Operation = operationName, RequestFingerprint = fingerprint, ResultJson = JsonSerializer.Serialize(result) }, ct);
            return result;
        }, ct);

    private static void Validate(Guid uid, int version)
    {
        if (uid == Guid.Empty || version <= 0) throw ProcurementOperationException.Validation("采购身份和版本无效。", "ExpectedVersion");
    }
    private static string Permission(ProcurementReversalOperation operation) => operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => "Confirm", ProcurementReversalOperation.DeleteLines => "Edit", ProcurementReversalOperation.Void => "Void",
        _ => throw new InvalidOperationException("未知采购反向操作。")
    };
    private static string Title(ProcurementReversalOperation operation) => operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => "取消采购确认", ProcurementReversalOperation.DeleteLines => "删除采购草稿行", _ => "作废采购单"
    };
}
