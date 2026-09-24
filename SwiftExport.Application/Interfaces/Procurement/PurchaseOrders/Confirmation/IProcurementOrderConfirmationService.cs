using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>读取当前确认依据，并按依据和订单版本执行采购确认。</summary>
public interface IProcurementOrderConfirmationService
{
    /// <summary>在一致性读取事务中取得当前确认依据。</summary>
    Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken ct = default);
    /// <summary>核对确认依据并原子确认采购订单。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command, CancellationToken ct = default);
}
