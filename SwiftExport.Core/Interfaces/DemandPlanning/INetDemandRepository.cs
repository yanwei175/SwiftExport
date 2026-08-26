using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>
/// 提供净需求拆解事实、工作台查询及计划追溯所需的数据访问能力。
/// </summary>
public interface INetDemandRepository
{
    Task<PagedResponseBase<NetDemandListItemDto>> SearchWorkspaceAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// 按独立来源导航条件汇总活动净需求，计划状态口径与工作台查询保持一致。
    /// </summary>
    Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchSourceGroupsAsync(
        NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<NetDemandListItemDto?> GetWorkspaceItemAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetDemandListItemDto>> GetWorkspaceFamilyAsync(
        Guid rootNetDemandId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetDemandListItemDto>> GetWorkspaceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId);
    Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId);
    Task<IReadOnlyList<NetDemandTraceItemDto>> GetByGrossDemandForTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands);
    Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason);
}
