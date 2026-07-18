using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Shipping;

public interface IPackingListWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<PackingListListItemDto>>> SearchAsync(PackingListSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<PackingListSourceDto>>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListCommandResultDto>> CreateDraftAsync(CreatePackingListDraftCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListCommandResultDto>> UpdateDraftAsync(Guid uid, UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListCommandResultDto>> VoidAsync(Guid uid, PackingListVoidCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListPreviewDto>> PostPreviewAsync(Guid uid, PackingListPreviewCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListCommandResultDto>> PostAsync(Guid uid, PackingListPostCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReverseCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListCommandResultDto>> ReverseAsync(Guid uid, PackingListReverseCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<PackingListExportDto>> GetExportAsync(Guid uid, CancellationToken cancellationToken = default);
}
