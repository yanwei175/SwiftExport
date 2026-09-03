using System.Text.Json;
using Mapster;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;

using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Procurement;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;

/// <summary>独立采购命令入口；协调专用规则、严格确认依据、持久化、回执和业务日志，不承接旧 Workspace。</summary>
public sealed partial class ProcurementOrderCommandService(
    ProcurementOrderTransaction transaction, IProcurementOrderRepository repository,
    ProcurementConfirmationBuilder confirmation, IDocumentNoService numbers,
    IBusinessOperationLogRepository logs, ICurrentUser user, TypeAdapterConfig mappings) : IProcurementOrderCommandService
{
    /// <summary>命令键先锁定再读取订单；身份和请求不一致不允许复用回执。</summary>
    private Task<ServiceResult<ProcurementOrderCommandResult>> ExecuteAsync<T>(
        Guid uid, Guid commandId, string operation, T command, Func<Task<ProcurementOrderCommandResult>> action, CancellationToken ct)
        => ExecuteAsync(uid, commandId, operation, operation, command, action, ct);

    /// <summary>允许多个业务命令共享同一权限能力，同时保持各自独立的幂等回执操作名。</summary>
    private Task<ServiceResult<ProcurementOrderCommandResult>> ExecuteAsync<T>(
        Guid uid, Guid commandId, string permissionOperation, string receiptOperation, T command, Func<Task<ProcurementOrderCommandResult>> action, CancellationToken ct)
        => transaction.ExecuteAsync(permissionOperation, async () =>
        {
            if (commandId == Guid.Empty) throw ProcurementOperationException.Validation("命令标识不能为空。", "CommandId");
            var fingerprint = ProcurementOrderPolicy.Fingerprint(new { uid, operation = receiptOperation, command });
            var receipt = await repository.GetReceiptAsync(commandId, ct);
            if (receipt is not null)
            {
                if (receipt.ActorUid != user.UserId || receipt.Operation != receiptOperation || receipt.RequestFingerprint != fingerprint)
                    throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.CommandConflict, "命令标识已用于不同用户、操作或请求内容。", "CommandId");
                return JsonSerializer.Deserialize<ProcurementOrderCommandResult>(receipt.ResultJson)
                    ?? throw new InvalidOperationException("原始命令回执损坏。");
            }
            var result = await action();
            if (await logs.InsertAsync(new BusinessOperationLog
            {
                Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId,
                CorrelationId = commandId.ToString("N"), Module = "Procurement.OrderManagement",
                AggregateType = "PurchaseOrder", AggregateUid = result.PurchaseOrderId, AggregateNoSnapshot = result.OrderNo,
                OperationType = receiptOperation, OperationName = "独立采购" + receiptOperation, Summary = "独立采购命令成功；详情见原始回执及确认依据。",
                UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now,
                // 取消原因属于业务审计事实；仅该命令把窄请求与结果一并保存，其他命令保持原结果形状。
                ChangedFieldsJson = receiptOperation == "CancelConfirmation"
                    ? JsonSerializer.Serialize(new { result, command })
                    : JsonSerializer.Serialize(result)
            }) != 1) throw new InvalidOperationException("采购业务日志写入失败。");
            await repository.InsertReceiptAsync(new ProcurementCommandReceipt
            {
                Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId, ActorUid = user.UserId!.Value,
                PurchaseOrderId = result.PurchaseOrderId, Operation = receiptOperation,
                RequestFingerprint = fingerprint, ResultJson = JsonSerializer.Serialize(result)
            }, ct);
            return result;
        }, ct);

    /// <summary>构造命令执行时的窄结果，保存到回执，后续重放不会受当前状态影响。</summary>
    private static ProcurementOrderCommandResult Result(PurchaseOrderHead head, List<PurchaseOrderLine> lines)
        => new()
        {
            PurchaseOrderId = head.Uid, OrderNo = head.OrderNo, Version = head.Version,
            ConfirmationStatus = head.ConfirmationStatus, ConfirmationId = head.ActiveConfirmationId,
            Lines = lines.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList()
        };
    /// <summary>读取本链订单，防止新命令接管历史单。</summary>
    private async Task<PurchaseOrderHead> RequireHeadAsync(Guid uid, CancellationToken ct)
        => await repository.GetHeadAsync(uid, ct) ?? throw new InvalidOperationException("独立采购单不存在。");
}
