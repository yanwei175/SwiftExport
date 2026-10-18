using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Manual;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Sources.Manual;

/// <summary>转发手工成品需求创建、修改和关闭命令，沿用现有请求防重复标记。</summary>
[ApiController]
[Route("api/demand-planning/gross-demands")]
public sealed class ManualGrossDemandController(IManualGrossDemandService manualService) : ControllerBase
{
    /// <summary>创建手工成品需求并返回详情。</summary>
    [HttpPost("manual")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> CreateManualAsync([FromBody] CreateManualGrossDemandRequest request)
        => manualService.CreateManualAsync(request);

    /// <summary>按身份和版本修改手工成品需求并返回详情。</summary>
    [HttpPut("manual/{uid:guid}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> UpdateManualAsync(
        [FromRoute] Guid uid, [FromBody] UpdateManualGrossDemandRequest request)
        => manualService.UpdateManualAsync(uid, request);

    /// <summary>按身份和版本关闭手工成品需求并返回详情。</summary>
    [HttpPost("manual/{uid:guid}/close")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<GrossDemandDetailDto>> CloseManualAsync(
        [FromRoute] Guid uid, [FromBody] CloseGrossDemandCommand command)
        => manualService.CloseManualAsync(uid, command);
}
