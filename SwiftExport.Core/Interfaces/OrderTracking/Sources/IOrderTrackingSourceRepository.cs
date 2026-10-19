using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.OrderTracking.Sources;
/// <summary>来源组独立分页，返回完整组条数和筛选命中数。</summary>
public interface IOrderTrackingSourceRepository
{
    /// <summary>先筛选完整集合再分组分页，不装载需求子表。</summary>
    Task<PagedResponseBase<OrderTrackingSourceGroupDto>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,DateTime businessDate,CancellationToken cancellationToken=default);
}
