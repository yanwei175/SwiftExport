using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory.Inbounds;

/// <summary>入库人工草稿命令的 HTTP 资源边界。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbounds")]
public sealed class InboundDraftController(IInboundDraftService draft) : ControllerBase
{
    /// <summary>创建人工草稿，不增加库存。</summary>
    [HttpPost("drafts")]
    public Task<ServiceResult<InboundDraftCommandResult>> CreateAsync([FromBody] CreateInboundDraftCommand command, CancellationToken token) => draft.CreateAsync(command, token);
    /// <summary>按整单版本保存人工草稿。</summary>
    [HttpPut("drafts/{uid:guid}")]
    public Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, [FromBody] UpdateInboundDraftCommand command, CancellationToken token) => draft.UpdateAsync(uid, command, token);
    /// <summary>软删除人工草稿并保留审计。</summary>
    [HttpPost("drafts/{uid:guid}/delete")]
    public Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, [FromBody] DeleteInboundDraftCommand command, CancellationToken token) => draft.DeleteAsync(uid, command, token);
}
