using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购从物料需求选取、安全追加候选及直接创建 API。</summary>
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

        /// <summary>按明确来源及供应商读取可追加草稿；创建时仍需持锁复核资格与版本。</summary>
        [Get("/append-candidates")]
        Task<ServiceResult<List<SafePurchaseDraftCandidateDto>>> GetAppendCandidatesAsync(
            string sourceType, Guid sourceUid, Guid supplierId,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按明确采购输入直接创建或追加草稿，服务端持锁重验容量，不依赖预览令牌。</summary>
        [Post("/create")]
        Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(
            [Body] CreatePurchaseDraftsCommand command,
            CancellationToken cancellationToken = default(CancellationToken));

    }
}
