using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.Sources;
/// <summary>来源主表查询用例，校验输入及区分失败与无匹配。</summary>
public interface IOrderTrackingSourceService
{
    /// <summary>读取来源组服务端分页。</summary>
    Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,CancellationToken cancellationToken=default);
}
