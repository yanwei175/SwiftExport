using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Maintenance;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Maintenance;

/// <summary>保持售后创建、保存及已确认单合法补充字段的原 HTTP 边界。</summary>
[ApiController, Authorize, Route("api/quality-control/after-sales")]
public sealed class AfterSalesMaintenanceController(IAfterSalesMaintenanceService service) : ControllerBase
{
    /// <summary>创建售后事实。</summary>
    [HttpPost, PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> CreateAsync([FromBody] SaveAfterSalesCommand command, CancellationToken token) => service.SaveAsync(command.Uid, command, true, token);

    /// <summary>保存售后事实或合法补充字段。</summary>
    [HttpPut("{uid:guid}"), PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, [FromBody] SaveAfterSalesCommand command, CancellationToken token) => service.SaveAsync(uid, command, false, token);
}
