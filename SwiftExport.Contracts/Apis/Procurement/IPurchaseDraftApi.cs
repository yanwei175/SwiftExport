using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购从物料需求选取、预览、创建及草稿生命周期 API。</summary>
    public interface IPurchaseDraftApi
    {
        /// <summary>查询仍有待采购物料的来源分组。</summary>
        [Get("/candidates/source-groups")]
        Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按来源身份或全部来源完整读取采购候选。</summary>
        [Post("/candidates")]
        Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(
            [Body] PurchaseDraftCandidateQuery request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按当前选择生成采购草稿预览。</summary>
        [Post("/preview")]
        Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(
            [Body] PurchaseDraftPreviewRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按有效预览创建或追加采购草稿。</summary>
        [Post("/create")]
        Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(
            [Body] CreatePurchaseDraftsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>取消尚无不安全下游事实的采购草稿。</summary>
        [Post("/{uid}/cancel")]
        Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(
            Guid uid,
            [Body] CancelPurchaseDraftCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>预览采购草稿减量对需求贡献的影响。</summary>
        [Post("/{uid}/release-preview")]
        Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(
            Guid uid,
            [Body] PurchaseDraftReleasePreviewRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按有效预览执行采购草稿减量。</summary>
        [Post("/{uid}/release")]
        Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(
            Guid uid,
            [Body] ReleasePurchaseDraftCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按来源身份读取采购承接追溯。</summary>
        [Get("/sources/{sourceType}/{sourceUid}/trace")]
        Task<ServiceResult<SourcePurchaseTraceDto>> GetSourceTraceAsync(
            string sourceType,
            Guid sourceUid,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
