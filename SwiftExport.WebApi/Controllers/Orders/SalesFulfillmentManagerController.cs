using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>销售履约 Manager 的只读 HTTP 边界。</summary>
[ApiController]
[Authorize]
[Route("api/sales-fulfillment/manager")]
public sealed class SalesFulfillmentManagerController(ISalesFulfillmentManagerService service) : ControllerBase
{
    /// <summary>返回销售订单粒度的履约摘要分页。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<SalesFulfillmentManagerListItemDto>>> SearchAsync(
        [FromBody] SalesFulfillmentManagerSearchRequest request,
        CancellationToken cancellationToken = default)
        => service.SearchAsync(request, cancellationToken);
}
