using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>
/// 净需求执行关系仓储接口。
/// </summary>
public interface INetDemandExecutionLinkRepository
{
    /// <summary>按物料需求锁定活动执行关系，保证采购承接数量不超额。</summary>
    Task<IReadOnlyList<PurchaseDemandCommitmentDto>> GetActiveByNetDemandIdsForUpdateAsync(
        IEnumerable<Guid> netDemandIds);
    Task<IReadOnlyList<MaterialDemandRollbackBlockerDto>> GetActiveRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds);

    /// <summary>按命令项锁定已有 Link，用于创建命令幂等判断。</summary>
    Task<IReadOnlyList<DemandExecutionLink>> GetByCommandItemIdsForUpdateAsync(
        IEnumerable<Guid> commandItemIds);

    /// <summary>显式批量插入采购 Link，保留 Application 预生成的 Uid。</summary>
    Task<int> InsertPurchaseLinksAsync(IEnumerable<DemandExecutionLink> links);

}
