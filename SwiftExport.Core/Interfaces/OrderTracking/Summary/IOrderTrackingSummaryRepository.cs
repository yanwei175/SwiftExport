using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
namespace SwiftExport.Core.Interfaces.OrderTracking.Summary;
/// <summary>批量摘要事实端口；每个维度一次读取，不逐需求调用明细。</summary>
public interface IOrderTrackingSummaryRepository
{
    /// <summary>读取基础摘要或指定有权维度；内部维度仅Base、Delivery、Material、Purchase、Receipt。</summary>
    Task<List<OrderTrackingDemandSummaryDto>> ReadDimensionAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,string dimension,DateTime businessDate,CancellationToken cancellationToken=default);
    /// <summary>在来源范围直接对采购行去重，不累加需求级计数。</summary>
    Task<List<OrderTrackingSourceSummaryDto>> ReadSourceReceiptsAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,DateTime businessDate,CancellationToken cancellationToken=default);
}
