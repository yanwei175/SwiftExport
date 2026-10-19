using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.OrderTracking.Summary;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.WebApi.Controllers.OrderTracking;
/// <summary>摘要查询HTTP边界；权限裁剪和数量规则由应用用例负责。</summary>
[ApiController]
[Route("api/order-tracking/sources")]
public sealed class OrderTrackingSummaryController(IOrderTrackingSummaryService service):ControllerBase
{
    /// <summary>批量读取当前页来源摘要。</summary>
    [HttpPost("summaries")]
    public Task<ServiceResult<OrderTrackingSourceSummaryBatchDto>> GetSourceSummariesAsync([FromBody]OrderTrackingSourceSummaryRequest request,CancellationToken cancellationToken)
        =>service.GetSourceSummariesAsync(request,cancellationToken);
    /// <summary>全量读取所选来源的需求摘要。</summary>
    [HttpGet("{sourceType}/{sourceGroupId:guid}/summaries")]
    public Task<ServiceResult<OrderTrackingSourceDemandSummariesDto>> GetSourceDemandSummariesAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken)
        =>service.GetSourceDemandSummariesAsync(sourceType,sourceGroupId,cancellationToken);
}
