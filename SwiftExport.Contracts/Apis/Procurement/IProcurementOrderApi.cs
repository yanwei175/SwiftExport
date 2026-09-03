using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>独立采购管理专用协议，与旧 purchase-orders Workspace 并存。</summary>
    public interface IProcurementOrderApi
    {
        /// <summary>单独取得已冻结确认快照，不按当前 BOM 重建。</summary>
        [Get("/{uid}/confirmation-snapshot")]
        Task<ServiceResult<ProcurementConfirmationSnapshotDto>> GetConfirmationSnapshotAsync(Guid uid, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购SearchAsync用例。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync([Body] ProcurementOrderSearchRequest request, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购GetDetailAsync用例。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购GetConfirmationContextAsync用例。</summary>
        [Get("/{uid}/confirmation-context")]
        Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购CreateAsync用例。</summary>
        [Post("/")]
        Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync([Body] CreateIndependentPurchaseOrderCommand command, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购SaveAsync用例。</summary>
        [Put("/{uid}/commercial")]
        Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, [Body] UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default);
        /// <summary>执行专用采购ConfirmAsync用例。</summary>
        [Post("/{uid}/confirm")]
        Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, [Body] ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default);
        /// <summary>取消尚无下游事实的独立采购确认，保留历史确认快照。</summary>
        [Post("/{uid}/cancel-confirmation")]
        Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, [Body] CancelProcurementOrderConfirmationCommand command, CancellationToken cancellationToken = default);
    }
}
