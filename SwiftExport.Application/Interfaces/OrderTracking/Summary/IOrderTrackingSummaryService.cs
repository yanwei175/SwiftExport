using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.Summary;
/// <summary>只读摘要用例，独立权限裁剪与失败状态不影响基础事实。</summary>
public interface IOrderTrackingSummaryService
{
    /// <summary>批量读取整个来源活动需求摘要。</summary>
    Task<ServiceResult<OrderTrackingSourceSummaryBatchDto>> GetSourceSummariesAsync(OrderTrackingSourceSummaryRequest request,CancellationToken cancellationToken=default);
    /// <summary>读取一个来源全部需求摘要，不分页。</summary>
    Task<ServiceResult<OrderTrackingSourceDemandSummariesDto>> GetSourceDemandSummariesAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken=default);
}
