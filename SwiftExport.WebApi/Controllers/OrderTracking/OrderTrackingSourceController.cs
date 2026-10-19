using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Sources;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>来源组搜索及组内成品需求匹配的 HTTP 边界。</summary>
[ApiController]
[Route("api/order-tracking/sources")]
public sealed class OrderTrackingSourceController(IOrderTrackingSourceService service) : ControllerBase
{
    /// <summary>读取命中来源组及完整来源条数。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync([FromBody]OrderTrackingSourceSearchRequest request,CancellationToken cancellationToken)
        =>service.SearchSourcesAsync(request,cancellationToken);

    /// <summary>按来源稳定身份复核本次查询命中的全部成品需求身份。</summary>
    [HttpPost("{sourceType}/{sourceGroupId:guid}/matches")]
    public Task<ServiceResult<OrderTrackingSourceMatchesDto>> GetSourceMatchesAsync(string sourceType,Guid sourceGroupId,[FromBody]OrderTrackingSourceMatchRequest request,CancellationToken cancellationToken)
        =>service.GetSourceMatchesAsync(sourceType,sourceGroupId,request,cancellationToken);
}
