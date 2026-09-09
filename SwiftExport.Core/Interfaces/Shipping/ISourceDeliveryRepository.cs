using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Shipping;

/// <summary>提供来源要求与正式交付事实的统一查询和锁定边界，不依赖物料需求或 Allocation 作为交付前提。</summary>
public interface ISourceDeliveryRepository
{
    /// <summary>分页查询仍有正式交付余额的认可来源要求。</summary>
    Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>读取单项来源要求及其正式交付净额。</summary>
    Task<SourceDemandDeliveryItemDto?> GetAsync(Guid grossDemandId, CancellationToken cancellationToken = default);

    /// <summary>按稳定顺序锁定来源要求并读取正式交付净额，供 PL 在最外层事务中复核。</summary>
    Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetForUpdateAsync(
        IEnumerable<Guid> grossDemandIds,
        CancellationToken cancellationToken = default);

    /// <summary>按来源单据锁定全部未删除要求及正式交付净额，供来源单据自行判断结案。</summary>
    Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetSourceForClosureForUpdateAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default);
}
