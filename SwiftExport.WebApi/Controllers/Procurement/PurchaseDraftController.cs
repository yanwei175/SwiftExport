using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Procurement;

/// <summary>采购从物料取数、预览创建及草稿生命周期的 HTTP 边界。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-drafts")]
public sealed class PurchaseDraftController(IPurchaseDraftService service) : ControllerBase
{
    /// <summary>查询仍含待采购物料的来源分组。</summary>
    [HttpGet("candidates/source-groups")]
    public Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken)
        => service.GetCandidateSourceGroupsAsync(cancellationToken);

    /// <summary>按来源或全部来源完整查询采购候选。</summary>
    [HttpPost("candidates")]
    public Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync([FromBody] PurchaseDraftCandidateQuery request, CancellationToken cancellationToken)
        => service.GetCandidatesAsync(request, cancellationToken);

    /// <summary>生成采购草稿预览。</summary>
    [HttpPost("preview")]
    public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync([FromBody] PurchaseDraftPreviewRequest request, CancellationToken cancellationToken)
        => service.PreviewAsync(request, cancellationToken);

    /// <summary>按有效预览创建或追加采购草稿。</summary>
    [HttpPost("create")]
    public Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync([FromBody] CreatePurchaseDraftsCommand command, CancellationToken cancellationToken)
        => service.CreateAsync(command, cancellationToken);

    /// <summary>取消尚无不安全下游事实的采购草稿。</summary>
    [HttpPost("{uid:guid}/cancel")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(Guid uid, [FromBody] CancelPurchaseDraftCommand command, CancellationToken cancellationToken)
        => service.CancelAsync(uid, command, cancellationToken);

    /// <summary>预览采购草稿减量影响。</summary>
    [HttpPost("{uid:guid}/release-preview")]
    public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(Guid uid, [FromBody] PurchaseDraftReleasePreviewRequest request, CancellationToken cancellationToken)
        => service.PreviewReleaseAsync(uid, request, cancellationToken);

    /// <summary>按有效预览执行采购草稿减量。</summary>
    [HttpPost("{uid:guid}/release")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(Guid uid, [FromBody] ReleasePurchaseDraftCommand command, CancellationToken cancellationToken)
        => service.ReleaseAsync(uid, command, cancellationToken);

    /// <summary>按来源身份读取采购承接追溯。</summary>
    [HttpGet("sources/{sourceType}/{sourceUid:guid}/trace")]
    public Task<ServiceResult<SourcePurchaseTraceDto>> GetSourceTraceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken)
        => service.GetSourceTraceAsync(sourceType, sourceUid, cancellationToken);
}
