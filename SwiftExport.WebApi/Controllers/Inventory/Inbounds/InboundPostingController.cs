using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Inbounds;

/// <summary>入库正向过账的 HTTP 资源边界。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbounds")]
public sealed class InboundPostingController(IInboundPostingService posting) : ControllerBase
{
    /// <summary>只读预览正向过账，正式提交仍重新校验。</summary>
    [HttpPost("{uid:guid}/post-preview")]
    public Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, [FromBody] InboundPostPreviewRequest request, CancellationToken token) => posting.PreviewPostAsync(uid, request, token);
    /// <summary>按命令和版本原子提交入库与库存，支持人工整单反向。</summary>
    [HttpPost("{uid:guid}/post")]
    public Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, [FromBody] PostInboundCommand command, CancellationToken token) => posting.PostAsync(uid, command, token);
}
