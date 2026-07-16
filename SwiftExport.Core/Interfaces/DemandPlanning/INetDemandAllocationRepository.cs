using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

public interface INetDemandAllocationRepository
{
    Task<NetDemandAllocation?> GetByIdAsync(Guid allocationId);
    Task<NetDemandAllocation?> GetByIdForUpdateAsync(Guid allocationId);
    Task<IReadOnlyList<NetDemandAllocation>> GetActiveByNetDemandIdsForUpdateAsync(IEnumerable<Guid> netDemandIds);
    Task<IReadOnlyList<NetDemandAllocationDto>> GetHistoryAsync(
        Guid netDemandId,
        CancellationToken cancellationToken = default);
    Task<RepositoryWriteResult> SupersedeAsync(Guid allocationId, int expectedVersion, string reason);
    Task<RepositoryWriteResult> AddReplacementAsync(NetDemandAllocation allocation);
    Task<IReadOnlyList<NetDemandAllocation>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId);
    Task<IReadOnlyList<RepositoryWriteResult>> AddInitialAsync(IEnumerable<NetDemandAllocation> allocations);
    Task<int> CancelRevisionAsync(Guid grossDemandId, int decompositionRevision, string reasonCode, string reason);
}
