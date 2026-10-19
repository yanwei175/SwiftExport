using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.Sources;
/// <summary>来源搜索与组内匹配查询用例，校验输入及区分失败与无匹配。</summary>
public interface IOrderTrackingSourceService
{
    /// <summary>读取来源组服务端分页。</summary>
    Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,CancellationToken cancellationToken=default);
    /// <summary>在指定来源内独立复核全部命中的成品需求身份，不分页或裁剪完整子表。</summary>
    Task<ServiceResult<OrderTrackingSourceMatchesDto>> GetSourceMatchesAsync(string sourceType,Guid sourceGroupId,OrderTrackingSourceMatchRequest request,CancellationToken cancellationToken=default);
}
