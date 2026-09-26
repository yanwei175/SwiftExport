using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Draft;

/// <summary>采购Draft用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementOrderDraftController(IProcurementOrderDraftService drafts) : ControllerBase
{
    /// <summary>转发采购CreateAsync用例，透传取消令牌。</summary>
    [HttpPost]
    public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync([FromBody] CreateIndependentPurchaseOrderCommand command, CancellationToken cancellationToken = default)
        => drafts.CreateAsync(command, cancellationToken);

    /// <summary>转发采购SaveAsync用例，透传取消令牌。</summary>
    [HttpPut("{uid}/commercial")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, [FromBody] UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default)
        => drafts.SaveAsync(uid, command, cancellationToken);
}
