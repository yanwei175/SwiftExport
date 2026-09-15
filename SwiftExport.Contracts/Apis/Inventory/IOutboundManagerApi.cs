using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>手工库存出库查询、草稿、正向过账及整单反过账契约，基址为 api/inventory/outbounds。</summary>
    public interface IOutboundManagerApi
    {
        /// <summary>核查手工出库整单反向及库存恢复。</summary>
        [Post("/{uid}/reverse-preview")]
        Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, [Body] OutboundReversePreviewRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按原单版本完整反向，不恢复草稿。</summary>
        [Post("/{uid}/reverse")]
        Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, [Body] ReverseOutboundCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>只读预览正向过账，不预占容量。</summary>
        [Post("/{uid}/post-preview")]
        Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, [Body] OutboundPostPreviewRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按版本与命令身份提交正向过账。</summary>
        [Post("/{uid}/post")]
        Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, [Body] PostOutboundCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按仓库分页读取正库存产品。</summary>
        [Post("/items/search")]
        Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync([Body] OutboundItemSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>分页读取出库摘要。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync([Body] OutboundManagerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取一张单据及其明细。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>原子建立人工草稿，不写库存。</summary>
        [Post("/drafts")]
        Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync([Body] CreateOutboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按整单版本原子更新草稿。</summary>
        [Put("/drafts/{uid}")]
        Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, [Body] UpdateOutboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按版本软删除人工草稿。</summary>
        [Post("/drafts/{uid}/delete")]
        Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, [Body] DeleteOutboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}
