using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Materials;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>Materials 区域独立 HTTP 边界，组合权限由端点过滤器强制执行。</summary>
[ApiController]
[Route("api/order-tracking/demands")]
public sealed class DemandTrackingMaterialController(IDemandTrackingMaterialService service) : ControllerBase
{
    /// <summary>仅加载当前区域，透传需求身份与请求取消。</summary>
    [HttpGet("{grossDemandId:guid}/materials")]
    public Task<ServiceResult<List<DemandTrackingMaterialDto>>> GetMaterialsAsync([FromRoute]Guid grossDemandId,CancellationToken cancellationToken)
        =>service.GetMaterialsAsync(grossDemandId,cancellationToken);
}
