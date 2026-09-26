using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement;

/// <summary>采购从物料取数、预览及创建的 HTTP 边界。</summary>
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

}
