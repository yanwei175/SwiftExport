using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Procurement.PurchaseOrders.Confirmation;

/// <summary>采购Confirmation用例的 HTTP 边界；保留既有路由和授权属性。</summary>
[ApiController]
[Authorize]
[Route("api/procurement/orders")]
public sealed class ProcurementOrderConfirmationController(IProcurementOrderConfirmationService confirmations) : ControllerBase
{
    /// <summary>转发采购GetConfirmationContextAsync用例，透传取消令牌。</summary>
    [HttpGet("{uid}/confirmation-context")]
    public Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken cancellationToken = default)
        => confirmations.GetConfirmationContextAsync(uid, cancellationToken);

    /// <summary>转发采购ConfirmAsync用例，透传取消令牌。</summary>
    [HttpPost("{uid}/confirm")]
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, [FromBody] ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default)
        => confirmations.ConfirmAsync(uid, command, cancellationToken);
}
