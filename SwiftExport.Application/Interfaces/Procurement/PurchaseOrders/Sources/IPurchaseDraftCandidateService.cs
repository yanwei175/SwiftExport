using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>需求转采购候选来源与待采购物料的只读边界。</summary>
public interface IPurchaseDraftCandidateService
{
    /// <summary>查询全部仍含待采购物料的来源分组。</summary>
    Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default);
    /// <summary>按稳定来源身份或全部来源完整查询待采购物料。</summary>
    Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default);
}
