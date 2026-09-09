using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

/// <summary>
/// 物料需求 HTTP 边界：物料用例转发新服务，旧计划和采购路由暂留兼容。
/// </summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class NetDemandWorkspaceController(INetDemandServices service, INetDemandWorkspaceService legacyService) : ControllerBase
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

    /// <summary>查询物料需求列表，沿用现有筛选和数量口径。</summary>
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

    /// <summary>读取物料详情；活动计划兼容字段随后续页面治理。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(
        Guid uid,
        CancellationToken cancellationToken)
        => service.GetDetailAsync(uid, cancellationToken);

    /// <summary>读取物料历史与执行关联，保持原追溯查询语义。</summary>
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

    /// <summary>待废弃：旧 Allocation 替代命令，独立计划退出后移除。</summary>
    [HttpPost("plans/replace")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(
        [FromBody] ReplaceNetDemandPlanCommand command)
        => legacyService.ReplacePlanAsync(command);

    /// <summary>待废弃：旧计划应用默认供应商，实际供应商选择归采购。</summary>
    [HttpPost("plans/apply-product-default")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(
        [FromBody] ApplyProductDefaultSupplierCommand command)
        => legacyService.ApplyDefaultSupplierAsync(command);

    /// <summary>待废弃：旧计划批量分配供应商，实际供应商选择归采购。</summary>
    [HttpPost("plans/batch-assign-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(
        [FromBody] BatchAssignSupplierCommand command)
        => legacyService.BatchAssignSupplierAsync(command);

    /// <summary>待迁移至采购领域：采购取数预览，当前仅兼容旧页面。</summary>
    [HttpPost("purchase-drafts/preview")]
    public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewPurchaseDraftsAsync(
        [FromBody] PurchaseDraftPreviewRequest request,
        CancellationToken cancellationToken)
        => legacyService.PreviewPurchaseDraftsAsync(request, cancellationToken);

    /// <summary>待迁移至采购领域：建单及关联写入，保留原事务与幂等。</summary>
    [HttpPost("purchase-drafts/create")]
    public Task<ServiceResult<CreatePurchaseDraftsResult>> CreatePurchaseDraftsAsync(
        [FromBody] CreatePurchaseDraftsCommand command,
        CancellationToken cancellationToken)
        => legacyService.CreatePurchaseDraftsAsync(command, cancellationToken);

    /// <summary>待迁移至采购领域：取消采购并处理关联，保留原取消规则。</summary>
    [HttpPost("purchase-drafts/{uid:guid}/cancel")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelPurchaseDraftAsync(
        Guid uid,
        [FromBody] CancelPurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => legacyService.CancelPurchaseDraftAsync(uid, command, cancellationToken);

    /// <summary>待迁移至采购领域：采购减量影响预览。</summary>
    [HttpPost("purchase-drafts/{uid:guid}/release-preview")]
    public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewPurchaseDraftReleaseAsync(
        Guid uid,
        [FromBody] PurchaseDraftReleasePreviewRequest request,
        CancellationToken cancellationToken)
        => legacyService.PreviewPurchaseDraftReleaseAsync(uid, request, cancellationToken);

    /// <summary>待迁移至采购领域：采购减量及关联更新。</summary>
    [HttpPost("purchase-drafts/{uid:guid}/release")]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleasePurchaseDraftAsync(
        Guid uid,
        [FromBody] ReleasePurchaseDraftCommand command,
        CancellationToken cancellationToken)
        => legacyService.ReleasePurchaseDraftAsync(uid, command, cancellationToken);

    /// <summary>待迁移至采购领域只读 Trace：按来源查询采购关联。</summary>
    [HttpGet("sources/{sourceType}/{sourceUid:guid}/purchase-trace")]
    public Task<ServiceResult<SourcePurchaseTraceDto>> GetPurchaseTraceBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken)
        => legacyService.GetPurchaseTraceBySourceAsync(sourceType, sourceUid, cancellationToken);
}
