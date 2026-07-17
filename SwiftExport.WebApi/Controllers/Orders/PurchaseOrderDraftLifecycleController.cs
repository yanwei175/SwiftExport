using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders;

[ApiController]
[Route("api/purchase-orders")]
public sealed class PurchaseOrderDraftLifecycleController(IPurchaseDraftLifecycleService service) : ControllerBase
{
    [HttpPost("drafts/{uid:guid}/cancel")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(
        Guid uid,
        [FromBody] CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => service.CancelAsync(uid, command, cancellationToken);

    [HttpPost("drafts/{uid:guid}/release-preview")]
    public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(
        Guid uid,
        [FromBody] PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken)
        => service.PreviewReleaseAsync(uid, request, cancellationToken);

    [HttpPost("drafts/{uid:guid}/release")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(
        Guid uid,
        [FromBody] ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => service.ReleaseAsync(uid, command, cancellationToken);
}
