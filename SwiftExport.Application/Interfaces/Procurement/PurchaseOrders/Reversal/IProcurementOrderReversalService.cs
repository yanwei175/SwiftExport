using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.Procurement;
/// <summary>采购取消确认、草稿删行和整单作废的权威入口。</summary>
public interface IProcurementOrderReversalService
{
    /// <summary>读取对应权限下的资格和影响，不修改业务事实。</summary>
    Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewAsync(Guid uid, ProcurementReversalOperation operation, ProcurementReversalPreviewRequest request, CancellationToken ct = default);
    /// <summary>只撤销确认状态，保留需求承接。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, CancelProcurementOrderConfirmationCommand command, CancellationToken ct = default);
    /// <summary>作废本单并解除其活动引用。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> VoidAsync(Guid uid, VoidProcurementOrderCommand command, CancellationToken ct = default);
    /// <summary>立即软删除选定草稿行并解除对应引用。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> DeleteLinesAsync(Guid uid, DeleteProcurementDraftLinesCommand command, CancellationToken ct = default);
    /// <summary>整单逻辑删除从未确认且没有下游历史的草稿，同事务释放全部活动引用。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> DeleteDraftAsync(Guid uid, DeleteProcurementDraftCommand command, CancellationToken ct = default);
}
