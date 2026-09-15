using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Services.Procurement.OrderManagement;
public sealed partial class ProcurementOrderCommandService
{
    /// <summary>旧内部调用统一转入反向服务，禁止保留第二套下游检查及状态写入。</summary>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(
        Guid uid, CancelProcurementOrderConfirmationCommand command, CancellationToken ct = default)
        => reversals.CancelConfirmationAsync(uid, command, ct);
}