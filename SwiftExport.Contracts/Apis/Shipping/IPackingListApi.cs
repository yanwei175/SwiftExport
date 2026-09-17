using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Shipping
{
    /// <summary>装箱单查询、草稿及执行追踪契约；基址 api/shipping/packing-lists。</summary>
    public interface IPackingListApi
    {
        /// <summary>箱单post-preview用例；服务端校验权限、版本及预览事实。</summary>
        [Post("/{uid}/post-preview")]
        Task<ServiceResult<PackingListPostingPreviewDto>> PostPreviewAsync(Guid uid, [Body] PackingListPostPreviewRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>箱单post用例；服务端校验权限、版本及预览事实。</summary>
        [Post("/{uid}/post")]
        Task<ServiceResult<PackingListPostingCommandResult>> PostAsync(Guid uid, [Body] PostPackingListCommand request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>箱单reverse-preview用例；服务端校验权限、版本及预览事实。</summary>
        [Post("/{uid}/reverse-preview")]
        Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid, [Body] PackingListReversePreviewRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>箱单reverse用例；服务端校验权限、版本及预览事实。</summary>
        [Post("/{uid}/reverse")]
        Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid, [Body] ReversePackingListCommand request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取实际过账及反向执行证据，不重新计算历史库存。</summary>
        [Get("/{uid}/trace")]
        Task<ServiceResult<PackingListTraceDto>> TraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>装箱单SearchAsync用例，服务端负责权限、版本及原子性。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>> SearchAsync([Body] PackingListManagerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>装箱单GetAsync用例，服务端负责权限、版本及原子性。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<PackingListDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>装箱单SearchSourcesAsync用例，服务端负责权限、版本及原子性。</summary>
        [Post("/sources/search")]
        Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>> SearchSourcesAsync([Body] PackingListSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>装箱单CreateAsync用例，服务端负责权限、版本及原子性。</summary>
        [Post("/drafts")]
        Task<ServiceResult<PackingListDraftCommandResult>> CreateAsync([Body] CreatePackingListDraftCommand request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>装箱单UpdateAsync用例，服务端负责权限、版本及原子性。</summary>
        [Put("/drafts/{uid}")]
        Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(Guid uid, [Body] UpdatePackingListDraftCommand request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>装箱单DeleteAsync用例，服务端负责权限、版本及原子性。</summary>
        [Post("/drafts/{uid}/delete")]
        Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(Guid uid, [Body] DeletePackingListDraftCommand request, CancellationToken cancellationToken = default(CancellationToken));
    }
}
