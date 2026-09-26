using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Draft;

/// <summary>采购Draft用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/purchase-drafts")]
public sealed class PurchaseDraftPreviewController(IPurchaseDraftPreviewService previews) : ControllerBase
{
    /// <summary>生成采购草稿预览。</summary>
    [HttpPost("preview")]
    public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync([FromBody] PurchaseDraftPreviewRequest request, CancellationToken cancellationToken)
        => previews.PreviewAsync(request, cancellationToken);
}
