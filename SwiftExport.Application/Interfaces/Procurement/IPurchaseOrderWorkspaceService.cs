using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

public interface IPurchaseOrderWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>> SearchAsync(PurchaseOrderSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<PurchaseOrderTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<ProcurementCommandResult>> UpdateDraftAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<ProcurementCommandResult>> ConfirmAsync(Guid uid, ConfirmPurchaseOrderCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<ProcurementCommandResult>> CancelConfirmationAsync(Guid uid, CancelPurchaseOrderConfirmationCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SupplierProgressReportDto>>> GetProgressAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<ProcurementCommandResult>> ReportProgressAsync(Guid uid, SupplierProgressReportCommand command, CancellationToken cancellationToken = default);
}
