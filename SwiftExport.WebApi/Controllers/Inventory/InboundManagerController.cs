using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

/// <summary>入库管理及正向过账 HTTP 边界；写入采用命令锁及幂等日志，不使用接口冷却过滤器，不开放反向或自动入库。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbounds")]
public sealed class InboundManagerController(IInboundManagerService service) : ControllerBase
{
    /// <summary>只读预览正向过账，正式提交仍重新校验。</summary>
    [HttpPost("{uid:guid}/post-preview")]
    public Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, [FromBody] InboundPostPreviewRequest request, CancellationToken token) => service.PreviewPostAsync(uid, request, token);
    /// <summary>按命令和版本原子提交入库与库存，不开放反向。</summary>
    [HttpPost("{uid:guid}/post")]
    public Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, [FromBody] PostInboundCommand command, CancellationToken token) => service.PostAsync(uid, command, token);
    /// <summary>转发分页查询。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync([FromBody] InboundManagerSearchRequest request, CancellationToken token) => service.SearchAsync(request, token);
    /// <summary>读取单据及明细。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token) => service.GetAsync(uid, token);
    /// <summary>创建人工草稿，不增加库存。</summary>
    [HttpPost("drafts")]
    public Task<ServiceResult<InboundDraftCommandResult>> CreateAsync([FromBody] CreateInboundDraftCommand command, CancellationToken token) => service.CreateAsync(command, token);
    /// <summary>按整单版本保存人工草稿。</summary>
    [HttpPut("drafts/{uid:guid}")]
    public Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, [FromBody] UpdateInboundDraftCommand command, CancellationToken token) => service.UpdateAsync(uid, command, token);
    /// <summary>软删除人工草稿并保留审计。</summary>
    [HttpPost("drafts/{uid:guid}/delete")]
    public Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, [FromBody] DeleteInboundDraftCommand command, CancellationToken token) => service.DeleteAsync(uid, command, token);
}
