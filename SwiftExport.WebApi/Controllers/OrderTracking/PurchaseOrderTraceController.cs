using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.OrderTracking;

/// <summary>采购追溯只读 HTTP 边界；细粒度订单权限由 Application 校验。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-order-trace")]
public sealed class PurchaseOrderTraceController(IPurchaseOrderTraceService service) : ControllerBase
{
    /// <summary>透传采购单 Uid 与请求取消，返回正式证据链。</summary>
    [HttpGet("{purchaseOrderId:guid}")]
    public Task<ServiceResult<PurchaseOrderTraceDto>> GetAsync(Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
        => service.GetAsync(purchaseOrderId, cancellationToken);
}
