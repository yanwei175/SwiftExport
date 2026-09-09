using Refit;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.DemandPlanning
{
    /// <summary>
    /// 净需求计划工作台 API。客户端 BaseAddress 为 api/demand-planning/net-demands。
    /// </summary>
    public interface INetDemandWorkspaceApi
    {
        /// <summary>查询可建立物料需求的来源需求候选。</summary>
        [Post("/source-demands/candidates/search")]
        Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
            [Body] SourceDemandCandidateSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>从来源需求建立物料需求。</summary>
        [Post("/source-demands/establish")]
        Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
            [Body] EstablishMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>回退来源对应的活动物料需求修订。</summary>
        [Post("/source-demands/rollback")]
        Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
            [Body] RollbackMaterialDemandsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
            [Body] NetDemandSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// 查询净需求来源导航分组，筛选口径独立于工作台顶部查询条件。
        /// </summary>
        [Post("/source-groups")]
        Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
            [Body] NetDemandSourceGroupSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
            Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}/trace")]
        Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(
            Guid uid,
            CancellationToken cancellationToken = default(CancellationToken));

        [Get("/source-demands/{grossDemandId}/trace")]
        Task<ServiceResult<GrossDemandTraceDto>> GetSourceDemandTraceAsync(
            Guid grossDemandId,
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
