using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>来源全量需求和客户候选HTTP边界，旧分页入口已退出。</summary>
[ApiController]
[Route("api/order-tracking")]
public sealed class OrderTrackingOverviewController(IOrderTrackingOverviewService service) : ControllerBase
{
    /// <summary>只绑定稳定来源身份，不接受主表筛选或分页。</summary>
    [HttpGet("sources/{sourceType}/{sourceGroupId:guid}/demands")]
    public Task<ServiceResult<OrderTrackingSourceDemandsDto>> GetSourceDemandsAsync([FromRoute]string sourceType,[FromRoute]Guid sourceGroupId,CancellationToken cancellationToken)
        =>service.GetSourceDemandsAsync(sourceType,sourceGroupId,cancellationToken);
    /// <summary>读取跟踪客户代码候选。</summary>
    [HttpGet("customer-codes")]
    public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken)=>service.GetCustomerCodesAsync(cancellationToken);
}
