using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Shipping;

[ApiController]
[Route("api/shipping/packing-lists")]
public sealed class PackingListWorkspaceController(IPackingListWorkspaceService service) : ControllerBase
{
    [HttpPost("search")] public Task<ServiceResult<PagedResponseBase<PackingListListItemDto>>> SearchAsync([FromBody] PackingListSearchRequest request, CancellationToken cancellationToken) => service.SearchAsync(request, cancellationToken);
    [HttpGet("{uid:guid}")] public Task<ServiceResult<PackingListDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken) => service.GetDetailAsync(uid, cancellationToken);
    [HttpPost("sources/search")] public Task<ServiceResult<PagedResponseBase<PackingListSourceDto>>> SearchSourcesAsync([FromBody] PackingListSourceSearchRequest request, CancellationToken cancellationToken) => service.SearchSourcesAsync(request, cancellationToken);
    [HttpPost("drafts"), PreventDuplicateRequest] public Task<ServiceResult<PackingListCommandResultDto>> CreateDraftAsync([FromBody] CreatePackingListDraftCommand command, CancellationToken cancellationToken) => service.CreateDraftAsync(command, cancellationToken);
    [HttpPut("drafts/{uid:guid}"), PreventDuplicateRequest] public Task<ServiceResult<PackingListCommandResultDto>> UpdateDraftAsync(Guid uid, [FromBody] UpdatePackingListDraftCommand command, CancellationToken cancellationToken) => service.UpdateDraftAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/void"), PreventDuplicateRequest] public Task<ServiceResult<PackingListCommandResultDto>> VoidAsync(Guid uid, [FromBody] PackingListVoidCommand command, CancellationToken cancellationToken) => service.VoidAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/post-preview")] public Task<ServiceResult<PackingListPreviewDto>> PostPreviewAsync(Guid uid, [FromBody] PackingListPreviewCommand command, CancellationToken cancellationToken) => service.PostPreviewAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/post"), PreventDuplicateRequest] public Task<ServiceResult<PackingListCommandResultDto>> PostAsync(Guid uid, [FromBody] PackingListPostCommand command, CancellationToken cancellationToken) => service.PostAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/reverse-preview")] public Task<ServiceResult<PackingListPreviewDto>> ReversePreviewAsync(Guid uid, [FromBody] PackingListReverseCommand command, CancellationToken cancellationToken) => service.ReversePreviewAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/reverse"), PreventDuplicateRequest] public Task<ServiceResult<PackingListCommandResultDto>> ReverseAsync(Guid uid, [FromBody] PackingListReverseCommand command, CancellationToken cancellationToken) => service.ReverseAsync(uid, command, cancellationToken);
    [HttpGet("{uid:guid}/trace")] public Task<ServiceResult<PackingListTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken) => service.GetTraceAsync(uid, cancellationToken);
    [HttpGet("{uid:guid}/export")] public Task<ServiceResult<PackingListExportDto>> GetExportAsync(Guid uid, CancellationToken cancellationToken) => service.GetExportAsync(uid, cancellationToken);
}
