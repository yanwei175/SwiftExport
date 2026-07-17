using Refit;
using SwiftExport.Contracts.DTOs.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Orders
{
    /// <summary>净需求专项创建的采购草稿取消与减量 API。</summary>
    public interface IPurchaseOrderDraftLifecycleApi
    {
        [Post("/drafts/{uid}/cancel")]
        Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(
            Guid uid,
            [Body] CancelPurchaseDraftCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/drafts/{uid}/release-preview")]
        Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(
            Guid uid,
            [Body] PurchaseDraftReleasePreviewRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/drafts/{uid}/release")]
        Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(
            Guid uid,
            [Body] ReleasePurchaseDraftCommand command,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
