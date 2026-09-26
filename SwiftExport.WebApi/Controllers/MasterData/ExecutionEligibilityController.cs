using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.MasterData;

/// <summary>主数据新业务资格启停的 HTTP 边界；实际管理员权限由服务在事务中重验。</summary>
[ApiController]
[Authorize]
[Route("api/master-data/execution-eligibility")]
public sealed class ExecutionEligibilityController(IExecutionEligibilityService service) : ControllerBase
{
    /// <summary>按预期版本启停指定主数据，不开放普通资料编辑或历史订单变更。</summary>
    [HttpPost("status")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<ExecutionEligibilityStatusResult>> SetStatusAsync(
        [FromBody] SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken)
        => service.SetStatusAsync(command, cancellationToken);
}
