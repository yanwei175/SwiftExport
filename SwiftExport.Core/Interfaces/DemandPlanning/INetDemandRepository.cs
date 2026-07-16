using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

public interface INetDemandRepository
{
    Task<PagedResponseBase<NetDemandListItemDto>> SearchWorkspaceAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<NetDemandListItemDto?> GetWorkspaceItemAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetDemandListItemDto>> GetWorkspaceFamilyAsync(
        Guid rootNetDemandId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId);
    Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId);
    Task<IReadOnlyList<NetDemandTraceItemDto>> GetByGrossDemandForTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands);
    Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason);
}
