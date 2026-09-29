using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.SalesOrders.Query;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.SalesOrders.Query;

/// <summary>提供按销售订单读取的只读履约详情。</summary>
[ApiController]
public sealed class SalesFulfillmentQueryController(ISalesFulfillmentQueryService service) : ControllerBase
{
    /// <summary>按订单 Uid 读取行级需求、采购、货好和正式交付。</summary>
    [HttpGet("api/sales-orders/fulfillment/{uid:guid}")]
    public Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync([FromRoute] Guid uid, CancellationToken cancellationToken)
        => service.GetAsync(uid, cancellationToken);
}
