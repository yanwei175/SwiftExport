using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Confirmation;

/// <summary>分别转发公共回执确认与旧协议取消确认。</summary>
[ApiController, Authorize, Route("api/quality-control/after-sales")]
public sealed class AfterSalesConfirmationController(IAfterSalesConfirmationService confirmations, IAfterSalesCancellationService cancellations) : ControllerBase
{
    /// <summary>确认正数量实物需求。</summary>
    [HttpPost("{uid:guid}/confirm-demand"), PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid, [FromBody] ConfirmAfterSalesDemandCommand command, CancellationToken token) => confirmations.ConfirmDemandAsync(uid, command, token);

    /// <summary>按原协议取消实物需求确认。</summary>
    [HttpPost("{uid:guid}/cancel-demand-confirmation"), PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, [FromBody] CancelAfterSalesDemandConfirmationCommand command, CancellationToken token) => cancellations.CancelDemandConfirmationAsync(uid, command, token);
}
