using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

public interface IOutboundWorkspaceService
{
    Task<ServiceResult<PagedResponseBase<OutboundListItemDto>>> SearchAsync(OutboundSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<OutboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<OutboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
}
