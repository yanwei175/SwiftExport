using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购从物料需求选取、预览及创建 API。</summary>
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

    }
}
