using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Outbounds;

/// <summary>转发人工出库草稿命令，不开放自动单据写入口。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/outbounds")]
public sealed class OutboundDraftController(IOutboundDraftService service) : ControllerBase
{
    /// <summary>创建人工草稿，不改变库存。</summary>
    [HttpPost("drafts")]
    public Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync([FromBody] CreateOutboundDraftCommand command, CancellationToken token) => service.CreateAsync(command, token);
    /// <summary>按整单版本更新人工草稿。</summary>
    [HttpPut("drafts/{uid:guid}")]
    public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, [FromBody] UpdateOutboundDraftCommand command, CancellationToken token) => service.UpdateAsync(uid, command, token);
    /// <summary>按整单版本软删除人工草稿。</summary>
    [HttpPost("drafts/{uid:guid}/delete")]
    public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, [FromBody] DeleteOutboundDraftCommand command, CancellationToken token) => service.DeleteAsync(uid, command, token);
}
