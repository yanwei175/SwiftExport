using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>保留交付调用边界；新交付事实尚未接入时明确不可用，不读取旧出库来源字段或返回虚构余额。</summary>
public sealed class SourceDeliveryService : ISourceDeliveryService
{
    private const string UnavailableCode = "DELIVERY_QUERY_UNAVAILABLE";
    private const string UnavailableMessage = "来源交付查询尚未接入，无法证明已交付数量、可交付余额或完成状态。";

    /// <summary>明确返回查询不可用，不以空列表代表没有待交付要求。</summary>
    public Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>.Fail(UnavailableCode, UnavailableMessage));
    }

    /// <summary>明确返回交付上下文不可用，不以零代替未知数量。</summary>
    public Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ServiceResult<SourceDemandDeliveryItemDto>.Fail(UnavailableCode, UnavailableMessage));
    }

    /// <summary>交付事实缺位时拒绝所有额度声明，包括空集合，不能作为库存动作的放行依据。</summary>
    public Task<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>> VerifyForUpdateAsync(
        IReadOnlyCollection<SourceDeliveryClaim> claims, bool requireInventoryContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto>>(
            new SourceDeliveryException(UnavailableCode, UnavailableMessage));
    }

    /// <summary>缺少正式交付证据时拒绝结案证明，不以来源状态或调用方数量推断完成。</summary>
    public Task VerifyClosureAsync(string sourceType, Guid sourceUid,
        IReadOnlyDictionary<Guid, decimal> expectedLines, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new SourceDeliveryException(UnavailableCode, UnavailableMessage));
    }
}
