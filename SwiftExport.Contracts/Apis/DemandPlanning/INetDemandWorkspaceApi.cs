using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>
    /// 净需求计划工作台 API。客户端 BaseAddress 为 api/demand-planning/net-demands。
    /// </summary>
    public interface INetDemandWorkspaceApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
            [Body] NetDemandSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
            Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}/trace")]
        Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(
            Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/plans/replace")]
        Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(
            [Body] ReplaceNetDemandPlanCommand command);

        [Post("/plans/apply-product-default")]
        Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
            [Body] ApplyProductDefaultSupplierCommand command);

        [Post("/plans/batch-assign-supplier")]
        Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
            [Body] BatchAssignSupplierCommand command);

        [Post("/purchase-drafts/preview")]
        Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewPurchaseDraftsAsync(
            [Body] PurchaseDraftPreviewRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/purchase-drafts/create")]
        Task<ServiceResult<CreatePurchaseDraftsResult>> CreatePurchaseDraftsAsync(
            [Body] CreatePurchaseDraftsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/purchase-drafts/{uid}/cancel")]
        Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelPurchaseDraftAsync(
            Guid uid,
            [Body] CancelPurchaseDraftCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/purchase-drafts/{uid}/release-preview")]
        Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewPurchaseDraftReleaseAsync(
            Guid uid,
            [Body] PurchaseDraftReleasePreviewRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/purchase-drafts/{uid}/release")]
        Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleasePurchaseDraftAsync(
            Guid uid,
            [Body] ReleasePurchaseDraftCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/sources/{sourceType}/{sourceUid}/purchase-trace")]
        Task<ServiceResult<SourcePurchaseTraceDto>> GetPurchaseTraceBySourceAsync(
            string sourceType,
            Guid sourceUid,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
