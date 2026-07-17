using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

public interface IInboundWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<InboundListItemDto>>> SearchAsync(InboundSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<InboundSourceDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundCommandResultDto>> CreateDraftAsync(CreateInboundDraftCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundCommandResultDto>> UpdateDraftAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundPostPreviewDto>> PostPreviewAsync(Guid uid, InboundPostCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundCommandResultDto>> PostAsync(Guid uid, InboundPostCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundPostPreviewDto>> ReversePreviewAsync(Guid uid, InboundReverseCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundCommandResultDto>> ReverseAsync(Guid uid, InboundReverseCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<InboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
}
