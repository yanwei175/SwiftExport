using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购查询、独立草稿创建及订单操作协议；详情、保存、确认和反向操作同时服务需求来源与独立采购。</summary>
    public interface IProcurementOrderApi
    {
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
        /// <summary>预览取消确认资格，保留物料需求引用。</summary>
        [Post("/{uid}/cancel-confirmation-preview")]
        Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewCancelConfirmationAsync(Guid uid, [Body] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default);
        /// <summary>取消确认回到草稿，原确认依据保留。</summary>
        [Post("/{uid}/cancel-confirmation")]
        Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, [Body] CancelProcurementOrderConfirmationCommand request, CancellationToken cancellationToken = default);
        /// <summary>预览整单作废及需求释放影响。</summary>
        [Post("/{uid}/void-preview")]
        Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewVoidAsync(Guid uid, [Body] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default);
        /// <summary>作废整单并解除本单活动需求引用。</summary>
        [Post("/{uid}/void")]
        Task<ServiceResult<ProcurementOrderCommandResult>> VoidAsync(Guid uid, [Body] VoidProcurementOrderCommand request, CancellationToken cancellationToken = default);
        /// <summary>预览删除选定草稿行及需求释放影响。</summary>
        [Post("/{uid}/delete-lines-preview")]
        Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewDeleteLinesAsync(Guid uid, [Body] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default);
        /// <summary>立即软删除选定草稿行，不接收其他商务修改。</summary>
        [Post("/{uid}/delete-lines")]
        Task<ServiceResult<ProcurementOrderCommandResult>> DeleteLinesAsync(Guid uid, [Body] DeleteProcurementDraftLinesCommand request, CancellationToken cancellationToken = default);
    }
}
