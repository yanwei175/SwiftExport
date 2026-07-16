using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

public interface INetDemandPlanCommandRepository
{
    Task<NetDemandPlanCommandRecord?> GetForUpdateAsync(Guid commandId, Guid originalAllocationId);
    Task<RepositoryWriteResult> AddAsync(NetDemandPlanCommandRecord record);
}
