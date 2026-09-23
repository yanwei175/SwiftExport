using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Inbounds;

/// <summary>入库人工整单反向的 HTTP 资源边界。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbounds")]
public sealed class InboundReversalController(IInboundReversalService reversal) : ControllerBase
{
    /// <summary>转发整单反过账只读预览。</summary>
    [HttpPost("{uid:guid}/reverse-preview")]
    public Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, [FromBody] InboundReversePreviewRequest request, CancellationToken token) => reversal.PreviewReverseAsync(uid, request, token);
    /// <summary>按原命令提交反过账，不使用接口冷却过滤器。</summary>
    [HttpPost("{uid:guid}/reverse")]
    public Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, [FromBody] ReverseInboundCommand command, CancellationToken token) => reversal.ReverseAsync(uid, command, token);
}
