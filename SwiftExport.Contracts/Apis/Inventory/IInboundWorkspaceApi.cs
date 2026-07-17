using Refit;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>客户端 BaseAddress 为 api/inventory/inbounds。</summary>
    public interface IInboundWorkspaceApi
    {
        [Post("/search")] Task<ServiceResult<PagedResponseBase<InboundListItemDto>>> SearchAsync([Body] InboundSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/{uid}")] Task<ServiceResult<InboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/sources/search")] Task<ServiceResult<PagedResponseBase<InboundSourceDto>>> SearchSourcesAsync([Body] InboundSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/drafts")] Task<ServiceResult<InboundCommandResultDto>> CreateDraftAsync([Body] CreateInboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Put("/drafts/{uid}")] Task<ServiceResult<InboundCommandResultDto>> UpdateDraftAsync(Guid uid, [Body] UpdateInboundDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/post-preview")] Task<ServiceResult<InboundPostPreviewDto>> PostPreviewAsync(Guid uid, [Body] InboundPostCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/post")] Task<ServiceResult<InboundCommandResultDto>> PostAsync(Guid uid, [Body] InboundPostCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/reverse-preview")] Task<ServiceResult<InboundPostPreviewDto>> ReversePreviewAsync(Guid uid, [Body] InboundReverseCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/reverse")] Task<ServiceResult<InboundCommandResultDto>> ReverseAsync(Guid uid, [Body] InboundReverseCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/{uid}/trace")] Task<ServiceResult<InboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
    }
}
