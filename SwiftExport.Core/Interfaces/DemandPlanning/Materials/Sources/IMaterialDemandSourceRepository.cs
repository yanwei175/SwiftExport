using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;

/// <summary>提供待建立来源与采购候选投影查询。</summary>
public interface IMaterialDemandSourceRepository
{
    /// <summary>汇总仍包含待采购物料的完整来源分组。</summary>
    Task<IReadOnlyList<PurchaseDraftCandidateSourceGroupDto>> GetPurchaseCandidateSourceGroupsAsync(
        CancellationToken cancellationToken = default);
    /// <summary>按来源或全部来源完整读取待采购物料，不分页。</summary>
    Task<IReadOnlyList<PurchaseDraftCandidateDto>> GetPurchaseCandidatesAsync(
        PurchaseDraftCandidateQuery request,
        CancellationToken cancellationToken = default);
    /// <summary>查询全部仍包含可建立来源行的来源单分组。</summary>
    Task<IReadOnlyList<SourceDemandCandidateGroupDto>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default);
    /// <summary>按来源类型和来源 Uid 完整查询一张来源单的全部可建立行。</summary>
    Task<IReadOnlyList<SourceDemandCandidateDto>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default);
}
