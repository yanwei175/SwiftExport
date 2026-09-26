using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Query;

/// <summary>采购Query用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementOrderQueryController(IProcurementOrderQueryService queries) : ControllerBase
{
    /// <summary>转发采购SearchAsync用例，透传取消令牌。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken cancellationToken = default)
        => queries.SearchAsync(request, cancellationToken);

    /// <summary>转发采购GetDetailAsync用例，透传取消令牌。</summary>
    [HttpGet("{uid}")]
    public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        => queries.GetDetailAsync(uid, cancellationToken);
}
