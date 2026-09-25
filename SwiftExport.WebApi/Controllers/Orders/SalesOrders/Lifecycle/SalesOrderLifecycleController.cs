using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Orders.SalesOrders.Lifecycle;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Orders.SalesOrders.Lifecycle;

/// <summary>转发原销售订单关闭 HTTP 用例。</summary>
[ApiController, Route("api/sales-orders/detail")]
public sealed class SalesOrderLifecycleController(ISalesOrderLifecycleService service) : ControllerBase
{
    /// <summary>关闭订单并按原规则退出来源需求。</summary>
    [HttpPost("{uid:guid}/close"), PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync([FromRoute] Guid uid, [FromBody] CloseSalesOrderRequest request)
        => service.CloseAsync(uid, request);
}
