using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>入库查询、草稿及正向过账契约，基址为 api/inventory/inbounds；不开放反向。</summary>
    public interface IInboundManagerApi
    {
        /// <summary>只读预览正向过账，不预占容量。</summary>
        [Post("/{uid}/post-preview")]
        Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, [Body] InboundPostPreviewRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按版本与命令身份提交正向过账。</summary>
        [Post("/{uid}/post")]
        Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, [Body] PostInboundCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>分页读取入库摘要。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync([Body] InboundManagerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取一张单据及其明细。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>原子建立人工草稿，不写库存。</summary>
        [Post("/drafts")]
        Task<ServiceResult<InboundDraftCommandResult>> CreateAsync([Body] CreateInboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按整单版本原子更新草稿。</summary>
        [Put("/drafts/{uid}")]
        Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, [Body] UpdateInboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按版本软删除人工草稿。</summary>
        [Post("/drafts/{uid}/delete")]
        Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, [Body] DeleteInboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}
