using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

public interface ISalesOrderWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<SalesOrderListItemDto>>> SearchAsync(SalesOrderSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<SalesOrderDocumentDto>> GetDocumentAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request);
    Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, SalesOrderCommandRequest request);
    Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, CancelSalesOrderConfirmationCommand request);
    Task<ServiceResult<SalesOrderDocumentDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request);
    Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request);
}
