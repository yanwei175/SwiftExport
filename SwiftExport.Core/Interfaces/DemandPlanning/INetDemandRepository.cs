using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning;

/// <summary>
/// 提供净需求拆解事实、工作台查询及计划追溯所需的数据访问能力。
/// </summary>
public interface INetDemandRepository
{
    /// <summary>查询不依赖计划分配表的物料需求工作台列表。</summary>
    Task<PagedResponseBase<MaterialDemandManagerItemDto>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>按来源事实汇总物料需求工作台左侧导航。</summary>
    Task<IReadOnlyList<NetDemandSourceGroupDto>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>汇总仍包含待采购物料的完整来源分组。</summary>
    Task<IReadOnlyList<PurchaseDraftCandidateSourceGroupDto>> GetPurchaseCandidateSourceGroupsAsync(
        CancellationToken cancellationToken = default);
    /// <summary>按来源或全部来源完整读取待采购物料，不分页。</summary>
    Task<IReadOnlyList<PurchaseDraftCandidateDto>> GetPurchaseCandidatesAsync(
        PurchaseDraftCandidateQuery request,
        CancellationToken cancellationToken = default);
    /// <summary>在调用方事务中锁定有效来源及其物料树。</summary>
    Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId);
    Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId);
    /// <summary>查询全部仍包含可建立来源行的来源单分组。</summary>
    Task<IReadOnlyList<SourceDemandCandidateGroupDto>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default);
    /// <summary>按来源类型和来源 Uid 完整查询一张来源单的全部可建立行。</summary>
    Task<IReadOnlyList<SourceDemandCandidateDto>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default);
    /// <summary>锁定来源对应的物料历史并取得下一修订号。</summary>
    Task<int> GetNextDecompositionRevisionForUpdateAsync(Guid grossDemandId);
    /// <summary>批量锁定并评估来源需求的物料历史，不跨域扫描执行、采购、入库或交付表。</summary>
    Task<IReadOnlyList<MaterialDemandSourceExitAssessment>> AssessSourceExitForUpdateAsync(
        IEnumerable<Guid> grossDemandIds);
    Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands);
    /// <summary>以乐观并发方式局部更新活动物料需求的推荐采购供应商。</summary>
    Task<int> UpdateSuggestedPurchaseSupplierAsync(
        Guid netDemandId,
        int expectedVersion,
        Guid? suggestedPurchaseSupplierId);
    Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason);
}
