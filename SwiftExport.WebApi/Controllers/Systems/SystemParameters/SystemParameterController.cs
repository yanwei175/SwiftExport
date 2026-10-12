using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Systems.SystemParameters;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Systems.SystemParameters;

/// <summary>轻量系统参数 HTTP 边界，活动账号和管理员规则由应用层复核。</summary>
[Authorize]
[ApiController]
[Route("api/system-parameters")]
public sealed class SystemParameterController(ISystemParameterService service) : ControllerBase
{
    /// <summary>读取全部公开注册参数。</summary>
    [HttpGet]
    public Task<ServiceResult<IReadOnlyList<SystemParameterDto>>> ListAsync(CancellationToken cancellationToken)
        => service.ListAsync(cancellationToken);

    /// <summary>按预期版本保存参数值。</summary>
    [HttpPut("{key}")]
    public Task<ServiceResult<SystemParameterDto>> UpdateAsync(string key, [FromBody] UpdateSystemParameterRequest request, CancellationToken cancellationToken)
        => service.UpdateAsync(key, request, cancellationToken);
}
