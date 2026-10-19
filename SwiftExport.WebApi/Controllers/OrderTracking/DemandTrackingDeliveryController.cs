using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Deliveries;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>Deliveries 区域独立 HTTP 边界，组合权限由端点过滤器强制执行。</summary>
[ApiController]
[Route("api/order-tracking/demands")]
public sealed class DemandTrackingDeliveryController(IDemandTrackingDeliveryService service) : ControllerBase
{
    /// <summary>仅加载当前区域，透传需求身份与请求取消。</summary>
    [HttpGet("{grossDemandId:guid}/deliveries")]
    public Task<ServiceResult<DemandTrackingDeliverySectionDto>> GetDeliveriesAsync([FromRoute]Guid grossDemandId,CancellationToken cancellationToken)
        =>service.GetDeliveriesAsync(grossDemandId,cancellationToken);
}
