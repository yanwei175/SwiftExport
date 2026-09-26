using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>定义来源需求本体的持久化、身份、版本和生命周期数据边界。</summary>
public interface IGrossDemandRepository
{
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
    Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion);
    Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason);
    /// <summary>删除已经由物料历史校验证明从未建立过物料事实的来源需求。</summary>
    Task<int> HardDeleteForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds);
    Task<int> VoidRolledBackForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds, string reason);
    Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason);
}
