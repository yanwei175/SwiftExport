using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>读取并锁定 GrossDemand 交付事实，交付净额以已过账 PackingList 行为权威。</summary>
public interface ISourceDeliveryQueryRepository
{
    /// <summary>验证统一交付查询所需的表和阶段一快照列已经部署。</summary>
    Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>分页查询具有客户和正剩余交付量的活动来源。</summary>
    Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>按标识读取来源及当前交付净额；不存在或已删除时返回空。</summary>
    Task<SourceDemandDeliveryItemDto?> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);

    /// <summary>在调用方事务内锁定来源和相关交付行并读取当前交付净额。</summary>
    Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default);
}
