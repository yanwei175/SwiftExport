using Refit;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SalesOrderApis
{
    /// <summary>
    /// 新销售订单工作区契约。
    /// </summary>
    public interface ISalesOrderWorkspaceApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<SalesOrderListItemDto>>> SearchAsync([Body] SalesOrderSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<SalesOrderDocumentDto>> GetDocumentAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/customer-codes")]
        Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default(CancellationToken));

        [Post("/products/search")]
        Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync([Body] SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/customers/search")]
        Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync([Body] SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/save")]
        Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync([Body] SaveSalesOrderRequest request);

        [Post("/{uid}/confirm")]
        Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, [Body] ConfirmSalesOrderCommand request);

        [Post("/{uid}/cancel-confirmation")]
        Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, [Body] CancelSalesOrderConfirmationCommand request);

        [Post("/{uid}/close")]
        Task<ServiceResult<SalesOrderDocumentDto>> CloseAsync(Guid uid, [Body] CloseSalesOrderRequest request);

        [Post("/{uid}/delete-draft")]
        Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, [Body] SalesOrderCommandRequest request);
    }
}
