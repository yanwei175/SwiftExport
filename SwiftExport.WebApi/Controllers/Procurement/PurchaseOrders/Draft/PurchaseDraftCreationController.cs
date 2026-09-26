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
public sealed class PurchaseDraftCreationController(IPurchaseDraftCreationService creation) : ControllerBase
{
    /// <summary>按有效预览创建或追加采购草稿。</summary>
    [HttpPost("create")]
    public Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync([FromBody] CreatePurchaseDraftsCommand command, CancellationToken cancellationToken)
        => creation.CreateAsync(command, cancellationToken);
}
