using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;

/// <summary>
/// 净需求执行关系仓储接口。
/// </summary>
public interface INetDemandExecutionLinkRepository
{
    /// <summary>按物料需求锁定活动执行关系，保证采购承接数量不超额。</summary>
    Task<IReadOnlyList<PurchaseDemandCommitmentDto>> GetActiveByNetDemandIdsForUpdateAsync(
        IEnumerable<Guid> netDemandIds);
    /// <summary>锁定仍有未释放贡献的执行关系，作为物料回退的唯一阻断事实。</summary>
    Task<IReadOnlyList<MaterialDemandRollbackBlockerDto>> GetRollbackBlockersAsync(
        IEnumerable<Guid> netDemandIds);

    /// <summary>按命令项锁定活动及已取消 Link；取消不释放命令身份，创建和补关联均不能复用。</summary>
    Task<IReadOnlyList<DemandExecutionLink>> GetByCommandItemIdsForUpdateAsync(
        IEnumerable<Guid> commandItemIds, CancellationToken cancellationToken = default);

    /// <summary>显式批量插入采购 Link，保留 Application 预生成的 Uid。</summary>
    Task<int> InsertPurchaseLinksAsync(IEnumerable<DemandExecutionLink> links, CancellationToken cancellationToken = default);

}
