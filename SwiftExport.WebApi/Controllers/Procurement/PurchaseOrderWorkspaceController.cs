using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement;

[ApiController]
[Route("api/procurement/purchase-orders")]
public sealed class PurchaseOrderWorkspaceController(IPurchaseOrderWorkspaceService service) : ControllerBase
{
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderListItemDto>>> SearchAsync([FromBody] PurchaseOrderSearchRequest request, CancellationToken cancellationToken) => service.SearchAsync(request, cancellationToken);
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken) => service.GetDetailAsync(uid, cancellationToken);
    [HttpGet("{uid:guid}/trace")]
    public Task<ServiceResult<PurchaseOrderTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken) => service.GetTraceAsync(uid, cancellationToken);
    [HttpPut("{uid:guid}/draft")]
    public Task<ServiceResult<ProcurementCommandResult>> UpdateDraftAsync(Guid uid, [FromBody] UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken) => service.UpdateDraftAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/confirm")]
    public Task<ServiceResult<ProcurementCommandResult>> ConfirmAsync(Guid uid, [FromBody] ConfirmPurchaseOrderCommand command, CancellationToken cancellationToken) => service.ConfirmAsync(uid, command, cancellationToken);
    [HttpPost("{uid:guid}/cancel-confirmation")]
    public Task<ServiceResult<ProcurementCommandResult>> CancelConfirmationAsync(Guid uid, [FromBody] CancelPurchaseOrderConfirmationCommand command, CancellationToken cancellationToken) => service.CancelConfirmationAsync(uid, command, cancellationToken);
    [HttpGet("{uid:guid}/progress")]
    public Task<ServiceResult<IReadOnlyList<SupplierProgressReportDto>>> GetProgressAsync(Guid uid, CancellationToken cancellationToken) => service.GetProgressAsync(uid, cancellationToken);
    [HttpPost("{uid:guid}/progress")]
    public Task<ServiceResult<ProcurementCommandResult>> ReportProgressAsync(Guid uid, [FromBody] SupplierProgressReportCommand command, CancellationToken cancellationToken) => service.ReportProgressAsync(uid, command, cancellationToken);
}
