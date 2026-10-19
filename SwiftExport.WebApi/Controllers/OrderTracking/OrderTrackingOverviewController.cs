using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.OrderTracking;

/// <summary>订单跟踪总览的只读 HTTP 入口，业务权限由端点过滤器统一校验。</summary>
[ApiController]
[Route("api/order-tracking/overview")]
public sealed class OrderTrackingOverviewController(IOrderTrackingOverviewService service) : ControllerBase
{
    /// <summary>按独立跟踪权限读取筛选候选，路由不依赖销售管理入口。</summary>
    [HttpGet("~/api/order-tracking/customer-codes")]
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken)
        => service.GetCustomerCodesAsync(cancellationToken);
    /// <summary>按成品需求行筛选与分页，并透传请求取消。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>> SearchAsync([FromBody] OrderTrackingSearchRequest request, CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);
}
