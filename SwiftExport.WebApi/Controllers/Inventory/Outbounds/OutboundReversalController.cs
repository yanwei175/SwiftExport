using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Outbounds;

/// <summary>转发人工出库整单反向，不编排 PackingList 自动单据。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/outbounds")]
public sealed class OutboundReversalController(IOutboundReversalService service) : ControllerBase
{
    /// <summary>只读证明整单反向的库存影响与阻断。</summary>
    [HttpPost("{uid:guid}/reverse-preview")]
    public Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, [FromBody] OutboundReversePreviewRequest request, CancellationToken token) => service.PreviewReverseAsync(uid, request, token);
    /// <summary>按原命令完整反向，不开放自动出库反向。</summary>
    [HttpPost("{uid:guid}/reverse")]
    public Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, [FromBody] ReverseOutboundCommand command, CancellationToken token) => service.ReverseAsync(uid, command, token);
}
