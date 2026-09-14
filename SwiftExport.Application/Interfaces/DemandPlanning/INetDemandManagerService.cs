using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>
/// 定义物料建立、回退与查询入口，不承接旧计划命令和采购写操作。
/// </summary>
public interface INetDemandManagerService
{
    /// <summary>查询不依赖计划分配表的物料需求工作台列表。</summary>
    Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>查询仅按来源事实汇总的工作台来源导航。</summary>
    Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>局部更新活动物料需求的推荐采购供应商，不改变采购与送达事实。</summary>
    Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken = default);
    /// <summary>查询尚可从来源需求建立物料需求的候选。</summary>
    Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>查询全部仍包含待建立来源行的来源单分组。</summary>
    Task<ServiceResult<List<SourceDemandCandidateGroupDto>>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default);
    /// <summary>按稳定来源身份完整查询一张来源单的全部待建立行。</summary>
    Task<ServiceResult<List<SourceDemandCandidateDto>>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default);
    /// <summary>从来源需求原子建立物料需求。</summary>
    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);
    /// <summary>安全回退来源对应的活动物料需求修订。</summary>
    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);
}
