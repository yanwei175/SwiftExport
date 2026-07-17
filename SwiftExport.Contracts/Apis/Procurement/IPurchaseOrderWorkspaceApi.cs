using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购与供应商跟单工作台 API。客户端 BaseAddress 为 api/procurement/purchase-orders。</summary>
    public interface IPurchaseOrderWorkspaceApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>> SearchAsync([Body] PurchaseOrderSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}/trace")]
        Task<ServiceResult<PurchaseOrderTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));

        [Put("/{uid}/draft")]
        Task<ServiceResult<ProcurementCommandResult>> UpdateDraftAsync(Guid uid, [Body] UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/{uid}/confirm")]
        Task<ServiceResult<ProcurementCommandResult>> ConfirmAsync(Guid uid, [Body] ConfirmPurchaseOrderCommand command, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/{uid}/cancel-confirmation")]
        Task<ServiceResult<ProcurementCommandResult>> CancelConfirmationAsync(Guid uid, [Body] CancelPurchaseOrderConfirmationCommand command, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}/progress")]
        Task<ServiceResult<IReadOnlyList<SupplierProgressReportDto>>> GetProgressAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/{uid}/progress")]
        Task<ServiceResult<ProcurementCommandResult>> ReportProgressAsync(Guid uid, [Body] SupplierProgressReportCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}
