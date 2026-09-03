using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Procurement.OrderManagement;

public sealed partial class ProcurementOrderCommandService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(
        Guid uid, CancelProcurementOrderConfirmationCommand command, CancellationToken ct = default)
    {
        if (command is null)
            return Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Fail(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Validation, "取消确认命令不能为空。", "command"));
        return ExecuteAsync(uid, command.CommandId, "Confirm", "CancelConfirmation", command, async () =>
        {
            var head = await RequireHeadAsync(uid, ct);
            if (command.ExpectedVersion <= 0 || head.Version != command.ExpectedVersion)
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict, "采购版本冲突，请刷新。", "ExpectedVersion");
            if (head.ConfirmationStatus != ConfirmationStatusEnum.已确认 || head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active)
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, "只有活动且已确认订单可以取消确认。");
            var reason = ProcurementOrderPolicy.Text(command.Reason, 1000, "Reason");
            if (string.IsNullOrWhiteSpace(reason))
                throw ProcurementOperationException.Validation("取消确认原因不能为空。", "Reason");
            if (await repository.HasDownstreamFactsAsync(uid, ct))
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.BusinessBlocked, "采购单已有入库或库存事实，不能取消确认。");
            var lines = await repository.GetLinesAsync(uid, ct);
            await repository.CancelConfirmationAsync(head, ct);
            head.Version++;
            head.ConfirmationStatus = ConfirmationStatusEnum.待确认;
            head.ActiveConfirmationId = null;
            head.ConfirmedDate = null;
            head.ConfirmedBy = null;
            return Result(head, lines);
        }, ct);
    }
}
