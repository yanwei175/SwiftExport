using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Procurement;

/// <summary>采购供应商跟进 HTTP 边界；不承载采购编辑、确认、取消或 Trace。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-order-progress")]
public sealed class PurchaseOrderProgressController(IPurchaseOrderProgressService service) : ControllerBase
{
    /// <summary>分页查询采购跟进头摘要。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        [FromBody] PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    /// <summary>读取采购行及完整反馈历史。</summary>
    [HttpGet("{purchaseOrderId:guid}/detail")]
    public Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
        => service.GetDetailAsync(purchaseOrderId, cancellationToken);

    /// <summary>追加供应商累计反馈；防重复窗口仅作为传输层补充，业务幂等由 CommandId 保证。</summary>
    [HttpPost("{purchaseOrderId:guid}/reports")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(
        Guid purchaseOrderId,
        [FromBody] ReportPurchaseOrderProgressCommand command,
        CancellationToken cancellationToken)
        => service.ReportAsync(purchaseOrderId, command, cancellationToken);
}
