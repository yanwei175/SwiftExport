using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Inventory;

[ApiController]
[Route("api/inventory/inbounds")]
public sealed class InboundWorkspaceController(IInboundWorkspaceService service) : ControllerBase
{
    [HttpPost("search")] public Task<ServiceResult<PagedResponseBase<InboundListItemDto>>> SearchAsync([FromBody] InboundSearchRequest request, CancellationToken cancellationToken) => service.SearchAsync(request, cancellationToken);
    [HttpGet("{uid:guid}")] public Task<ServiceResult<InboundDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken) => service.GetDetailAsync(uid, cancellationToken);
    [HttpPost("sources/search")] public Task<ServiceResult<PagedResponseBase<InboundSourceDto>>> SearchSourcesAsync([FromBody] InboundSourceSearchRequest request, CancellationToken cancellationToken) => service.SearchSourcesAsync(request, cancellationToken);
    [HttpPost("drafts")][PreventDuplicateRequest] public Task<ServiceResult<InboundCommandResultDto>> CreateDraftAsync([FromBody] CreateInboundDraftCommand command, CancellationToken cancellationToken) => service.CreateDraftAsync(command, cancellationToken);
    [HttpPut("drafts/{uid:guid}")][PreventDuplicateRequest] public Task<ServiceResult<InboundCommandResultDto>> UpdateDraftAsync(Guid uid, [FromBody] UpdateInboundDraftCommand command, CancellationToken cancellationToken) => service.UpdateDraftAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/post-preview")] public Task<ServiceResult<InboundPostPreviewDto>> PostPreviewAsync(Guid uid, [FromBody] InboundPostCommand command, CancellationToken cancellationToken) => service.PostPreviewAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/post")][PreventDuplicateRequest] public Task<ServiceResult<InboundCommandResultDto>> PostAsync(Guid uid, [FromBody] InboundPostCommand command, CancellationToken cancellationToken) => service.PostAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/reverse-preview")] public Task<ServiceResult<InboundPostPreviewDto>> ReversePreviewAsync(Guid uid, [FromBody] InboundReverseCommand command, CancellationToken cancellationToken) => service.ReversePreviewAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/reverse")][PreventDuplicateRequest] public Task<ServiceResult<InboundCommandResultDto>> ReverseAsync(Guid uid, [FromBody] InboundReverseCommand command, CancellationToken cancellationToken) => service.ReverseAsync(uid, command, cancellationToken);
    [HttpGet("{uid:guid}/trace")] public Task<ServiceResult<InboundTraceDto>> TraceAsync(Guid uid, CancellationToken cancellationToken) => service.GetTraceAsync(uid, cancellationToken);
}
