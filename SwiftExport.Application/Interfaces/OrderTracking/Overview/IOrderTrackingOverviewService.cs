using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.Overview;
/// <summary>来源完整子表与客户候选只读用例。</summary>
public interface IOrderTrackingOverviewService
{
    /// <summary>校验稳定身份后返回全来源需求，不接收主表筛选或分页。</summary>
    Task<ServiceResult<OrderTrackingSourceDemandsDto>> GetSourceDemandsAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken=default);
    /// <summary>读取跟踪筛选客户代码候选。</summary>
    Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken=default);
}
