using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.OrderTracking.Sources;
/// <summary>来源组独立分页及组内命中身份读取，区分完整组与筛选命中集合。</summary>
public interface IOrderTrackingSourceRepository
{
    /// <summary>先筛选完整集合再分组分页，不装载需求子表。</summary>
    Task<PagedResponseBase<OrderTrackingSourceGroupDto>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,DateTime businessDate,CancellationToken cancellationToken=default);
    /// <summary>按来源稳定身份和同一需求行上的条件返回完整去重命中身份。</summary>
    Task<List<Guid>> GetSourceMatchesAsync(string sourceType,Guid sourceGroupId,OrderTrackingSourceMatchRequest request,DateTime businessDate,CancellationToken cancellationToken=default);
}
