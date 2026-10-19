using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
namespace SwiftExport.Core.Interfaces.OrderTracking.Overview;
/// <summary>读取选中来源的完整需求及客户候选，无子表分页能力。</summary>
public interface IOrderTrackingOverviewRepository
{
    /// <summary>仅按稳定来源身份读取全部非软删除需求。</summary>
    Task<OrderTrackingSourceDemandsDto> GetSourceDemandsAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken=default);
    /// <summary>读取已有需求客户的当前代码候选。</summary>
    Task<List<string>> GetCustomerCodesAsync(CancellationToken cancellationToken=default);
}
