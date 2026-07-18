using Refit;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Shipping
{
    public interface IPackingListWorkspaceApi
    {
        [Post("/search")] Task<ServiceResult<PagedResponseBase<PackingListListItemDto>>> SearchAsync([Body] PackingListSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/{uid}")] Task<ServiceResult<PackingListDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/sources/search")] Task<ServiceResult<PagedResponseBase<PackingListSourceDto>>> SearchSourcesAsync([Body] PackingListSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/drafts")] Task<ServiceResult<PackingListCommandResultDto>> CreateDraftAsync([Body] CreatePackingListDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Put("/drafts/{uid}")] Task<ServiceResult<PackingListCommandResultDto>> UpdateDraftAsync(Guid uid, [Body] UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/void")] Task<ServiceResult<PackingListCommandResultDto>> VoidAsync(Guid uid, [Body] PackingListVoidCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/post-preview")] Task<ServiceResult<PackingListPreviewDto>> PostPreviewAsync(Guid uid, [Body] PackingListPreviewCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/post")] Task<ServiceResult<PackingListCommandResultDto>> PostAsync(Guid uid, [Body] PackingListPostCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/reverse-preview")] Task<ServiceResult<PackingListPreviewDto>> ReversePreviewAsync(Guid uid, [Body] PackingListReverseCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/reverse")] Task<ServiceResult<PackingListCommandResultDto>> ReverseAsync(Guid uid, [Body] PackingListReverseCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/{uid}/trace")] Task<ServiceResult<PackingListTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/{uid}/export")] Task<ServiceResult<PackingListExportDto>> GetExportAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
    }
}
