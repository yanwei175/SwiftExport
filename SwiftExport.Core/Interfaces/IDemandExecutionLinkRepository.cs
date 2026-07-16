using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces;

/// <summary>
/// 净需求执行关系仓储接口。
/// </summary>
public interface IDemandExecutionLinkRepository
{
    Task<IReadOnlyList<GrossDemandRollbackBlockerDto>> GetActiveRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds);
    Task<IReadOnlyList<DemandExecutionTraceItemDto>> GetTraceByNetDemandIdsAsync(
        IEnumerable<Guid> netDemandIds,
        CancellationToken cancellationToken = default);
}
