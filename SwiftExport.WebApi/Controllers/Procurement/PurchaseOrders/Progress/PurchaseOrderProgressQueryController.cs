using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Progress;

/// <summary>采购Progress用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-order-progress")]
public sealed class PurchaseOrderProgressQueryController(IPurchaseOrderProgressQueryService queries) : ControllerBase
{
    /// <summary>分页查询采购跟进头摘要。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
        [FromBody] PurchaseOrderProgressSearchRequest request,
        CancellationToken cancellationToken)
        => queries.SearchAsync(request, cancellationToken);

    /// <summary>读取采购行及完整反馈历史。</summary>
    [HttpGet("{purchaseOrderId:guid}/detail")]
    public Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
        => queries.GetDetailAsync(purchaseOrderId, cancellationToken);
}
