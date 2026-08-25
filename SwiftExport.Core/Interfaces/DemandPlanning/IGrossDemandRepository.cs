using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

public interface IGrossDemandRepository
{
    Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid);
    Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid);
    Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands);
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
