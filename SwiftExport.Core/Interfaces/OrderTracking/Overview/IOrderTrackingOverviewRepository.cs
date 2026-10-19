using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.OrderTracking.Overview;

/// <summary>按成品需求行查询订单跟踪基础总览，不读取下游执行事实。</summary>
public interface IOrderTrackingOverviewRepository
{
    /// <summary>读取已有成品需求客户的当前非空代码，去重稳定排序。</summary>
    Task<List<string>> GetCustomerCodesAsync(CancellationToken cancellationToken = default);
    /// <summary>按交集筛选稳定分页，并独立返回总记录数。</summary>
    Task<PagedResponseBase<OrderTrackingListItemDto>> SearchAsync(OrderTrackingSearchRequest request, CancellationToken cancellationToken = default);
}
