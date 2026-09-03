using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement;

/// <summary>采购管理页新链查询 HTTP 边界；快速命令继续由既有采购订单路由承载。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/order-manager")]
public sealed class PurchaseOrderManagerController(IPurchaseOrderManagerQueryService service) : ControllerBase
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
