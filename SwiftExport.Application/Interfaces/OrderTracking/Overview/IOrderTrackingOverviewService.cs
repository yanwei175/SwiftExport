using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.OrderTracking.Overview;

/// <summary>校验订单跟踪总览查询条件并返回只读需求事实。</summary>
public interface IOrderTrackingOverviewService
{
    /// <summary>读取跟踪筛选使用的客户代码候选，不要求销售或客户维护权限。</summary>
    Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default);
    /// <summary>按成品需求粒度查询分页，不修改请求或数据库事实。</summary>
    Task<ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>> SearchAsync(OrderTrackingSearchRequest request, CancellationToken cancellationToken = default);
}
