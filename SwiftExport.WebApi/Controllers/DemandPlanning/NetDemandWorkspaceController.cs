using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class NetDemandWorkspaceController(INetDemandWorkspaceService service) : ControllerBase
{
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        [FromBody] NetDemandSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken)
        => service.GetDetailAsync(uid, cancellationToken);

    [HttpGet("{uid:guid}/trace")]
    public Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(
        Guid uid,
        CancellationToken cancellationToken)
        => service.GetTraceAsync(uid, cancellationToken);

    [HttpPost("plans/replace")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(
        [FromBody] ReplaceNetDemandPlanCommand command)
        => service.ReplacePlanAsync(command);

    [HttpPost("plans/apply-product-default")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
        [FromBody] ApplyProductDefaultSupplierCommand command)
        => service.ApplyDefaultSupplierAsync(command);

    [HttpPost("plans/batch-assign-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
        [FromBody] BatchAssignSupplierCommand command)
        => service.BatchAssignSupplierAsync(command);

    [HttpPost("purchase-drafts/preview")]
    public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewPurchaseDraftsAsync(
        [FromBody] PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
        => service.PreviewPurchaseDraftsAsync(request, cancellationToken);

    [HttpPost("purchase-drafts/create")]
    public Task<ServiceResult<CreatePurchaseDraftsResult>> CreatePurchaseDraftsAsync(
        [FromBody] CreatePurchaseDraftsCommand command,
        CancellationToken cancellationToken)
        => service.CreatePurchaseDraftsAsync(command, cancellationToken);

    [HttpPost("purchase-drafts/{uid:guid}/cancel")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelPurchaseDraftAsync(
        Guid uid,
        [FromBody] CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => service.CancelPurchaseDraftAsync(uid, command, cancellationToken);

    [HttpPost("purchase-drafts/{uid:guid}/release-preview")]
    public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewPurchaseDraftReleaseAsync(
        Guid uid,
        [FromBody] PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken)
        => service.PreviewPurchaseDraftReleaseAsync(uid, request, cancellationToken);

    [HttpPost("purchase-drafts/{uid:guid}/release")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleasePurchaseDraftAsync(
        Guid uid,
        [FromBody] ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => service.ReleasePurchaseDraftAsync(uid, command, cancellationToken);

    [HttpGet("sources/{sourceType}/{sourceUid:guid}/purchase-trace")]
    public Task<ServiceResult<SourcePurchaseTraceDto>> GetPurchaseTraceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken)
        => service.GetPurchaseTraceBySourceAsync(sourceType, sourceUid, cancellationToken);
}
