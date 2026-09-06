using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders;

/// <summary>销售履约 Detail 的只读 HTTP 边界。</summary>
[ApiController]
[Authorize]
[Route("api/sales-fulfillment/detail")]
public sealed class SalesFulfillmentDetailController(ISalesFulfillmentDetailService service) : ControllerBase
{
    /// <summary>返回指定销售订单的履约、组件采购和订单级采购关联。</summary>
    [HttpGet("{salesOrderId:guid}")]
    public Task<ServiceResult<SalesFulfillmentDetailDto>> GetAsync(
        Guid salesOrderId,
        CancellationToken cancellationToken = default)
        => service.GetAsync(salesOrderId, cancellationToken);
}
