using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

public interface INetDemandRepository
{
    Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId);
    Task<IReadOnlyList<NetDemandTraceItemDto>> GetByGrossDemandForTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands);
    Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason);
}
