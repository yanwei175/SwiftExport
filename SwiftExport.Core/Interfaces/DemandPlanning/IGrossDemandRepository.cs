using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>定义来源需求的持久化、计划查询、交付额度和生命周期数据边界。</summary>
public interface IGrossDemandRepository
{
    /// <summary>查询认可来源的可交付投影；物料需求及 Allocation 不参与准入。</summary>
    Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchDeliverableAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>读取单项来源需求的当前交付上下文。</summary>
    Task<SourceDemandDeliveryItemDto?> GetDeliveryContextAsync(Guid grossDemandId, CancellationToken cancellationToken = default);
    /// <summary>按固定顺序锁定来源需求并返回正式客户出库净额，用于事务内额度复核。</summary>
    Task<IReadOnlyList<SourceDemandDeliveryItemDto>> GetDeliveryContextsForUpdateAsync(
        IEnumerable<Guid> grossDemandIds,
        CancellationToken cancellationToken = default);
    Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid);
    Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid);
    Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands);
    Task<RepositoryWriteResult> UpdateSourceDemandAsync(GrossDemand demand, int expectedVersion);
    Task<RepositoryWriteResult> UpdateDecompositionStateAsync(
        Guid uid,
        int expectedVersion,
        GrossDemandDecompositionStatusEnum status,
        int decompositionRevision,
        DateTime? decomposedAt);
    Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion);
    Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason);
    Task<bool> HasSourceCancellationBlockerAsync(string sourceType, Guid sourceUid, IEnumerable<Guid> sourceLineIds);
    Task<bool> HasInvalidSourceCancellationHistoryAsync(IEnumerable<Guid> grossDemandIds);
    Task<IReadOnlyDictionary<Guid, int>> GetNetDemandHistoryCountsForUpdateAsync(IEnumerable<Guid> grossDemandIds);
    Task<int> HardDeleteNeverDecomposedAsync(IEnumerable<Guid> grossDemandIds);
    Task<int> VoidRolledBackForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds, string reason);
    Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason);
}
