using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

[ApiController]
[Route("api/demand-planning/gross-demands")]
public sealed class GrossDemandWorkspaceController(IGrossDemandWorkspaceService service) : ControllerBase
{
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(
        [FromBody] GrossDemandSearchRequest request, CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    [HttpPost("source-groups/search")]
    public Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        [FromBody] GrossDemandSearchRequest request, CancellationToken cancellationToken)
        => service.SearchSourceGroupsAsync(request, cancellationToken);

    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync([FromRoute] Guid uid, CancellationToken cancellationToken)
        => service.GetDetailAsync(uid, cancellationToken);

    [HttpGet("{uid:guid}/trace")]
    public Task<ServiceResult<GrossDemandTraceDto>> GetTraceAsync([FromRoute] Guid uid, CancellationToken cancellationToken)
        => service.GetTraceAsync(uid, cancellationToken);

    [HttpPost("decompose")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDecomposeResult>> DecomposeAsync([FromBody] GrossDemandDecomposeCommand command)
        => service.DecomposeAsync(command);

    [HttpPost("rollback-decomposition")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandRollbackResult>> RollbackAsync([FromBody] GrossDemandRollbackCommand command)
        => service.RollbackAsync(command);

    [HttpPost("manual")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync([FromBody] CreateManualGrossDemandRequest request)
        => service.CreateManualAsync(request);

    [HttpPut("manual/{uid:guid}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(
        [FromRoute] Guid uid, [FromBody] UpdateManualGrossDemandRequest request)
        => service.UpdateManualAsync(uid, request);

    [HttpPost("manual/{uid:guid}/close")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(
        [FromRoute] Guid uid, [FromBody] CloseGrossDemandCommand command)
        => service.CloseManualAsync(uid, command);
}
