using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>定义采购从物料需求取数、预览和创建的唯一应用边界。</summary>
public interface IPurchaseDraftService
{
    /// <summary>查询全部仍含待采购物料的来源分组。</summary>
    Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default);
    /// <summary>按稳定来源身份或全部来源完整查询待采购物料。</summary>
    Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default);
    /// <summary>生成采购草稿预览。</summary>
    Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default);
    /// <summary>按有效预览创建采购草稿并接受需求贡献。</summary>
    Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default);
}
