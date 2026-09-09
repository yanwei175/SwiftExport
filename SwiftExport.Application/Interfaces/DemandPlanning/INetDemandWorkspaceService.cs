using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>
/// 编排净需求工作台查询、计划调整、采购草稿及追溯能力。
/// </summary>
public interface INetDemandWorkspaceService
{
    /// <summary>查询尚可从来源需求建立物料需求的候选。</summary>
    Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>从来源需求原子建立物料需求。</summary>
    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);
    /// <summary>安全回退来源对应的活动物料需求修订。</summary>
    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// 查询左侧来源导航使用的独立来源分组。
    /// </summary>
    Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>由物料领域读取一条来源需求的物料及执行追溯。</summary>
    Task<ServiceResult<GrossDemandTraceDto>> GetSourceDemandTraceAsync(Guid grossDemandId, CancellationToken cancellationToken = default);
    Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(ReplaceNetDemandPlanCommand command);
    Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(ApplyProductDefaultSupplierCommand command);
    Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(BatchAssignSupplierCommand command);
    Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewPurchaseDraftsAsync(
        PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<CreatePurchaseDraftsResult>> CreatePurchaseDraftsAsync(
        CreatePurchaseDraftsCommand command,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelPurchaseDraftAsync(
        Guid uid,
        CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewPurchaseDraftReleaseAsync(
        Guid uid,
        PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleasePurchaseDraftAsync(
        Guid uid,
        ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<SourcePurchaseTraceDto>> GetPurchaseTraceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default);
}
