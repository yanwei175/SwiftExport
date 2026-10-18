using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;

/// <summary>在调用方事务中锁定、修订、写入和回退物料需求事实。</summary>
public interface IMaterialDemandDocumentRepository
{
    /// <summary>在调用方事务中锁定有效来源及其物料树。</summary>
    Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId);
    /// <summary>按来源身份锁定活动物料需求并稳定排序。</summary>
    Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId);
    /// <summary>锁定来源对应的物料历史并取得下一修订号。</summary>
    Task<int> GetNextDecompositionRevisionForUpdateAsync(Guid grossDemandId);
    /// <summary>批量锁定并评估成品需求的物料历史，不跨域扫描执行、采购、入库或交付表。</summary>
    Task<IReadOnlyList<MaterialDemandSourceExitAssessment>> AssessSourceExitForUpdateAsync(
        IEnumerable<Guid> grossDemandIds);
    /// <summary>批量写入同次拆解事实并返回各行写入结果。</summary>
    Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands);
    /// <summary>以乐观并发方式局部更新活动物料需求的推荐采购供应商。</summary>
    Task<int> UpdateSuggestedPurchaseSupplierAsync(
        Guid netDemandId,
        int expectedVersion,
        Guid? suggestedPurchaseSupplierId);
    /// <summary>按来源和修订号将活动物料需求整组标记为回退。</summary>
    Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason);
}
