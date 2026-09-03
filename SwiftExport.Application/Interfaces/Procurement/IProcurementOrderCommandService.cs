using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;
/// <summary>新采购管理的创建、商务保存和确认命令，不承接需求计划或供应商跟进。</summary>
public interface IProcurementOrderCommandService
{
    /// <summary>创建独立订单并保存原始幂等结果。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken ct = default);
    /// <summary>以头行预期版本保存商务白名单。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken ct = default);
    /// <summary>验证完整确认依据并原子固化快照。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command, CancellationToken ct = default);
    /// <summary>取消无下游事实的确认；确认批次只解除活动引用，不删除历史快照。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, CancelProcurementOrderConfirmationCommand command, CancellationToken ct = default);
}
