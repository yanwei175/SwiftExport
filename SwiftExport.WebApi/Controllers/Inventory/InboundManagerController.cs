using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Inventory;

/// <summary>入库管理 HTTP 边界；写入采用服务端 CommandId 锁及幂等日志，不使用按接口冷却过滤器；不开放过账、反向或自动入库。</summary>
[ApiController]
[Authorize]
[Route("api/inventory/inbounds")]
public sealed class InboundManagerController(IInboundManagerService service) : ControllerBase
{
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
