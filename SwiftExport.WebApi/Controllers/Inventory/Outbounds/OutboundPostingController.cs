using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Outbounds;

/// <summary>转发人工出库过账预览和正式命令。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/outbounds")]
public sealed class OutboundPostingController(IOutboundPostingService service) : ControllerBase
{
    /// <summary>只读预览库存影响，正式提交仍重新校验。</summary>
    [HttpPost("{uid:guid}/post-preview")]
    public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, [FromBody] OutboundPostPreviewRequest request, CancellationToken token) => service.PreviewPostAsync(uid, request, token);
    /// <summary>按命令和版本整单过账。</summary>
    [HttpPost("{uid:guid}/post")]
    public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, [FromBody] PostOutboundCommand command, CancellationToken token) => service.PostAsync(uid, command, token);
}
