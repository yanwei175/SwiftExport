using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>独立采购管理专用协议，与旧 purchase-orders Workspace 并存。</summary>
    public interface IProcurementOrderApi
    {
        /// <summary>单独取得已冻结确认快照，不按当前 BOM 重建。</summary>
        [Get("/api/procurement/orders/{uid}/confirmation-snapshot")]
        Task<ServiceResult<ProcurementConfirmationSnapshotDto>> GetConfirmationSnapshotAsync(Guid uid, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购SearchAsync用例。</summary>
        [Post("/api/procurement/orders/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync([Body] ProcurementOrderSearchRequest request, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购GetDetailAsync用例。</summary>
        [Get("/api/procurement/orders/{uid}")]
        Task<ServiceResult<ProcurementOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购GetConfirmationContextAsync用例。</summary>
        [Get("/api/procurement/orders/{uid}/confirmation-context")]
        Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购CreateAsync用例。</summary>
        [Post("/api/procurement/orders")]
        Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync([Body] CreateIndependentPurchaseOrderCommand command, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购SaveAsync用例。</summary>
        [Put("/api/procurement/orders/{uid}/commercial")]
        Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, [Body] SaveProcurementOrderCommand command, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购ConfirmAsync用例。</summary>
        [Post("/api/procurement/orders/{uid}/confirm")]
        Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, [Body] ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default);
    }
}
