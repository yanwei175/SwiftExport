using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Manual;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

[ApiController]
[Route("api/demand-planning/gross-demands")]
/// <summary>转发来源需求查询与手工维护 HTTP 用例，保持既有路由和返回契约。</summary>
public sealed class GrossDemandWorkspaceController(
    IGrossDemandQueryService queryService,
    IManualGrossDemandService manualService) : ControllerBase
{
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(
        [FromBody] GrossDemandSearchRequest request, CancellationToken cancellationToken)
        => queryService.SearchAsync(request, cancellationToken);

    [HttpPost("source-groups/search")]
    public Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        [FromBody] GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken)
        => queryService.SearchSourceGroupsAsync(request, cancellationToken);

    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync([FromRoute] Guid uid, CancellationToken cancellationToken)
        => queryService.GetDetailAsync(uid, cancellationToken);

    [HttpPost("manual")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync([FromBody] CreateManualGrossDemandRequest request)
        => manualService.CreateManualAsync(request);

    [HttpPut("manual/{uid:guid}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(
        [FromRoute] Guid uid, [FromBody] UpdateManualGrossDemandRequest request)
        => manualService.UpdateManualAsync(uid, request);

    [HttpPost("manual/{uid:guid}/close")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(
        [FromRoute] Guid uid, [FromBody] CloseGrossDemandCommand command)
        => manualService.CloseManualAsync(uid, command);
}
