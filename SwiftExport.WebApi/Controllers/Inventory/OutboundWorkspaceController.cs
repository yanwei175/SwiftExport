using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

[ApiController]
[Route("api/inventory/outbounds")]
public sealed class OutboundWorkspaceController(IOutboundWorkspaceService service) : ControllerBase
{
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<OutboundListItemDto>>> SearchAsync([FromBody] OutboundSearchRequest request, CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<OutboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken)
        => service.GetDetailAsync(uid, cancellationToken);

    [HttpGet("{uid:guid}/trace")]
    public Task<ServiceResult<OutboundTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken)
        => service.GetTraceAsync(uid, cancellationToken);
}
