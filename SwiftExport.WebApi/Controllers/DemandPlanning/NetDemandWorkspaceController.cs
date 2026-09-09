using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

/// <summary>
/// 暴露净需求工作台的查询、计划命令、采购草稿和追溯 HTTP 边界。
/// </summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class NetDemandWorkspaceController(INetDemandWorkspaceService service) : ControllerBase
{
    /// <summary>查询由物料领域计算的来源需求建立候选。</summary>
    [HttpPost("source-demands/candidates/search")]
    public Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        [FromBody] SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchSourceDemandCandidatesAsync(request, cancellationToken);

    /// <summary>从来源需求原子建立物料需求。</summary>
    [HttpPost("source-demands/establish")]
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        [FromBody] EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => service.EstablishFromSourcesAsync(command, cancellationToken);

    /// <summary>安全回退来源对应的活动物料需求修订。</summary>
    [HttpPost("source-demands/rollback")]
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        [FromBody] RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => service.RollbackSourceEstablishmentAsync(command, cancellationToken);

    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        [FromBody] NetDemandSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchAsync(request, cancellationToken);

    /// <summary>
    /// 查询净需求左侧来源导航分组。
    /// </summary>
    [HttpPost("source-groups")]
    public Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        [FromBody] NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken)
        => service.SearchSourceGroupsAsync(request, cancellationToken);

    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken)
        => service.GetDetailAsync(uid, cancellationToken);

    [HttpGet("{uid:guid}/trace")]
    public Task<ServiceResult<NetDemandTraceDto>> GetTraceAsync(
        Guid uid,
        CancellationToken cancellationToken)
        => service.GetTraceAsync(uid, cancellationToken);

    /// <summary>查询来源需求对应的物料及执行追溯。</summary>
    [HttpGet("source-demands/{grossDemandId:guid}/trace")]
    public Task<ServiceResult<GrossDemandTraceDto>> GetSourceDemandTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken)
        => service.GetSourceDemandTraceAsync(grossDemandId, cancellationToken);

    [HttpPost("plans/replace")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(
        [FromBody] ReplaceNetDemandPlanCommand command)
        => service.ReplacePlanAsync(command);

    [HttpPost("plans/apply-product-default")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
        [FromBody] ApplyProductDefaultSupplierCommand command)
        => service.ApplyDefaultSupplierAsync(command);

    [HttpPost("plans/batch-assign-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
        [FromBody] BatchAssignSupplierCommand command)
        => service.BatchAssignSupplierAsync(command);

    [HttpPost("purchase-drafts/preview")]
    public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewPurchaseDraftsAsync(
        [FromBody] PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
        => service.PreviewPurchaseDraftsAsync(request, cancellationToken);

    [HttpPost("purchase-drafts/create")]
    public Task<ServiceResult<CreatePurchaseDraftsResult>> CreatePurchaseDraftsAsync(
        [FromBody] CreatePurchaseDraftsCommand command,
        CancellationToken cancellationToken)
        => service.CreatePurchaseDraftsAsync(command, cancellationToken);

    [HttpPost("purchase-drafts/{uid:guid}/cancel")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelPurchaseDraftAsync(
        Guid uid,
        [FromBody] CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => service.CancelPurchaseDraftAsync(uid, command, cancellationToken);

    [HttpPost("purchase-drafts/{uid:guid}/release-preview")]
    public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewPurchaseDraftReleaseAsync(
        Guid uid,
        [FromBody] PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken)
        => service.PreviewPurchaseDraftReleaseAsync(uid, request, cancellationToken);

    [HttpPost("purchase-drafts/{uid:guid}/release")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleasePurchaseDraftAsync(
        Guid uid,
        [FromBody] ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => service.ReleasePurchaseDraftAsync(uid, command, cancellationToken);

    [HttpGet("sources/{sourceType}/{sourceUid:guid}/purchase-trace")]
    public Task<ServiceResult<SourcePurchaseTraceDto>> GetPurchaseTraceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken)
        => service.GetPurchaseTraceBySourceAsync(sourceType, sourceUid, cancellationToken);
}
