using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 净需求执行关系仓储接口。
/// </summary>
public interface IDemandExecutionLinkRepository
{
    Task<IReadOnlyList<NetDemandExecutionLinkDto>> GetByNetDemandIdsAsync(
        IEnumerable<Guid> netDemandIds,
        bool activeOnly,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NetDemandExecutionLinkDto>> GetActiveByAllocationIdsForUpdateAsync(
        IEnumerable<Guid> allocationIds);
    Task<IReadOnlyList<GrossDemandRollbackBlockerDto>> GetActiveRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds);
    Task<IReadOnlyList<DemandExecutionTraceItemDto>> GetTraceByNetDemandIdsAsync(
        IEnumerable<Guid> netDemandIds,
        CancellationToken cancellationToken = default);
}
