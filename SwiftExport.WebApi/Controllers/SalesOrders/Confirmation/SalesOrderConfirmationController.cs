using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.SalesOrders.Confirmation;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.SalesOrders.Confirmation;

/// <summary>分别转发公共确认和旧协议取消确认，保留原动作级认证标记。</summary>
[ApiController, Route("api/sales-orders/detail")]
public sealed class SalesOrderConfirmationController(ISalesOrderConfirmationService confirmations, ISalesOrderCancellationService cancellations) : ControllerBase
{
    /// <summary>确认订单并接收来源需求。</summary>
    [HttpPost("{uid:guid}/confirm"), Authorize, PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync([FromRoute] Guid uid, [FromBody] ConfirmSalesOrderCommand request)
        => confirmations.ConfirmAsync(uid, request);

    /// <summary>按现行协议安全取消确认。</summary>
    [HttpPost("{uid:guid}/cancel-confirmation"), PreventDuplicateRequest]
    public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync([FromRoute] Guid uid, [FromBody] CancelSalesOrderConfirmationCommand request)
        => cancellations.CancelConfirmationAsync(uid, request);
}
