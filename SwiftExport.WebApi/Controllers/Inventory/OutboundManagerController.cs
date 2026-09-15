using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

/// <summary>手工出库管理及整单反过账 HTTP 边界；写入采用命令锁及幂等日志，不使用接口冷却过滤器，不开放自动出库。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/outbounds")]
public sealed class OutboundManagerController(IOutboundManagerService service) : ControllerBase
{
    /// <summary>预览手工出库整单反过账。</summary>
    [HttpPost("{uid:guid}/reverse-preview")]
    public Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, [FromBody] OutboundReversePreviewRequest request, CancellationToken token) => service.PreviewReverseAsync(uid, request, token);
    /// <summary>按原命令完整反向，不开放自动出库反向。</summary>
    [HttpPost("{uid:guid}/reverse")]
    public Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, [FromBody] ReverseOutboundCommand command, CancellationToken token) => service.ReverseAsync(uid, command, token);
    /// <summary>只读预览正向过账，正式提交仍重新校验。</summary>
    [HttpPost("{uid:guid}/post-preview")]
    public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, [FromBody] OutboundPostPreviewRequest request, CancellationToken token) => service.PreviewPostAsync(uid, request, token);
    /// <summary>按命令和版本原子提交出库与库存。</summary>
    [HttpPost("{uid:guid}/post")]
    public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, [FromBody] PostOutboundCommand command, CancellationToken token) => service.PostAsync(uid, command, token);
    /// <summary>按仓库分页读取正库存产品。</summary>
    [HttpPost("items/search")]
    public Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync([FromBody] OutboundItemSearchRequest request, CancellationToken token) => service.SearchItemsAsync(request, token);
    /// <summary>转发分页查询。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync([FromBody] OutboundManagerSearchRequest request, CancellationToken token) => service.SearchAsync(request, token);
    /// <summary>读取单据及明细。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token) => service.GetAsync(uid, token);
    /// <summary>创建人工草稿，不增加库存。</summary>
    [HttpPost("drafts")]
    public Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync([FromBody] CreateOutboundDraftCommand command, CancellationToken token) => service.CreateAsync(command, token);
    /// <summary>按整单版本保存人工草稿。</summary>
    [HttpPut("drafts/{uid:guid}")]
    public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, [FromBody] UpdateOutboundDraftCommand command, CancellationToken token) => service.UpdateAsync(uid, command, token);
    /// <summary>软删除人工草稿并保留审计。</summary>
    [HttpPost("drafts/{uid:guid}/delete")]
    public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, [FromBody] DeleteOutboundDraftCommand command, CancellationToken token) => service.DeleteAsync(uid, command, token);
}
