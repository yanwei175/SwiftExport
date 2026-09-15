using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Shipping
{
    /// <summary>装箱单查询及草稿契约；基址 api/shipping/packing-lists，后端未实现，不含过账入口。</summary>
    public interface IPackingListApi
    {
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
