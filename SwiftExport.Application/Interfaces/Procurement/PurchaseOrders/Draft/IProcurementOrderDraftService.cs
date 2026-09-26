using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>独立采购草稿创建和商务保存命令。</summary>
public interface IProcurementOrderDraftService
{
    /// <summary>创建独立订单并保存可重放结果。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken ct = default);
    /// <summary>按头行预期版本保存商务白名单。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken ct = default);
}
