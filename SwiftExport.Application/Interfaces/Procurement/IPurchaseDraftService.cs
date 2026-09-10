using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>定义采购从物料需求取数、预览创建和草稿生命周期的唯一应用边界。</summary>
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
    /// <summary>取消安全采购草稿并协调需求贡献。</summary>
    Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(Guid uid, CancelPurchaseDraftCommand command, CancellationToken cancellationToken = default);
    /// <summary>预览采购草稿减量影响。</summary>
    Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(Guid uid, PurchaseDraftReleasePreviewRequest request, CancellationToken cancellationToken = default);
    /// <summary>执行采购草稿减量并协调需求贡献。</summary>
    Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(Guid uid, ReleasePurchaseDraftCommand command, CancellationToken cancellationToken = default);
    /// <summary>按来源读取采购承接追溯。</summary>
    Task<ServiceResult<SourcePurchaseTraceDto>> GetSourceTraceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken = default);
}
