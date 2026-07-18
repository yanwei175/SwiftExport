using Refit;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Inventory
{
    public interface IOutboundWorkspaceApi
    {
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<OutboundListItemDto>>> SearchAsync([Body] OutboundSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}")]
        Task<ServiceResult<OutboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));

        [Get("/{uid}/trace")]
        Task<ServiceResult<OutboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
    }
}
