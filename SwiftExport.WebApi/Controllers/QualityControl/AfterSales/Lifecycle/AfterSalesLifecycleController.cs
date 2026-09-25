using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Lifecycle;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Lifecycle;

/// <summary>保持售后结案、重开和作废的原 HTTP 边界。</summary>
[ApiController, Authorize, Route("api/quality-control/after-sales")]
public sealed class AfterSalesLifecycleController(IAfterSalesLifecycleService service) : ControllerBase
{
    /// <summary>按原规则结案；正数量单继续阻断待交付核验。</summary>
    [HttpPost("{uid:guid}/close"), PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, [FromBody] CloseAfterSalesCommand command, CancellationToken token) => service.CloseAsync(uid, command, token);

    /// <summary>重开已结案售后单。</summary>
    [HttpPost("{uid:guid}/reopen"), PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, [FromBody] ReopenAfterSalesCommand command, CancellationToken token) => service.ReopenAsync(uid, command, token);

    /// <summary>按原来源退出规则作废。</summary>
    [HttpPost("{uid:guid}/void"), PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, [FromBody] VoidAfterSalesCommand command, CancellationToken token) => service.VoidAsync(uid, command, token);
}
