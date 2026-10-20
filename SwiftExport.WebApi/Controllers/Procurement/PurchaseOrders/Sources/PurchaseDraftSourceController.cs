using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Sources;

/// <summary>采购Sources用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-drafts")]
public sealed class PurchaseDraftSourceController(IPurchaseDraftCandidateService candidates) : ControllerBase
{
    /// <summary>按稳定来源身份和实际供应商读取安全追加草稿，不复用宽泛管理列表。</summary>
    [HttpGet("append-candidates")]
    public Task<ServiceResult<List<SafePurchaseDraftCandidateDto>>> GetAppendCandidatesAsync([FromQuery]string sourceType,[FromQuery]Guid sourceUid,[FromQuery]Guid supplierId,CancellationToken cancellationToken=default)
        => candidates.GetAppendCandidatesAsync(sourceType,sourceUid,supplierId,cancellationToken);
    /// <summary>查询仍含待采购物料的来源分组。</summary>
    [HttpGet("candidates/source-groups")]
    public Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken)
        => candidates.GetCandidateSourceGroupsAsync(cancellationToken);

    /// <summary>按来源或全部来源完整查询采购候选。</summary>
    [HttpPost("candidates")]
    public Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync([FromBody] PurchaseDraftCandidateQuery request, CancellationToken cancellationToken)
        => candidates.GetCandidatesAsync(request, cancellationToken);
}
