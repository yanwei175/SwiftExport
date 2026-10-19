using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.DemandDetail;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.OrderTracking;

/// <summary>独立成品需求基础明细 HTTP 入口，不装载后续跟踪区域。</summary>
[ApiController]
[Route("api/order-tracking/demands")]
public sealed class DemandTrackingHeaderController(IDemandTrackingHeaderService service) : ControllerBase
{
    /// <summary>按成品需求稳定身份加载只读基础明细。</summary>
    [HttpGet("{grossDemandId:guid}")]
    public Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync([FromRoute] Guid grossDemandId, CancellationToken cancellationToken)
        => service.GetDemandHeaderAsync(grossDemandId, cancellationToken);
}
