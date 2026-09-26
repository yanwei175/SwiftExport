using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Query;

/// <summary>采购Query用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/order-manager")]
public sealed class PurchaseOrderManagerQueryController(IPurchaseOrderManagerQueryService service) : ControllerBase
{
    /// <summary>转发采购管理头摘要分页查询并透传请求取消。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>> SearchAsync(
        [FromBody] PurchaseOrderManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return service.SearchAsync(request, cancellationToken);
    }

    /// <summary>转发指定采购单的管理行摘要查询并透传请求取消。</summary>
    [HttpGet("{purchaseOrderId:guid}/lines")]
    public Task<ServiceResult<IReadOnlyList<PurchaseOrderManagerLineDto>>> GetLinesAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
        => service.GetLinesAsync(purchaseOrderId, cancellationToken);
}
