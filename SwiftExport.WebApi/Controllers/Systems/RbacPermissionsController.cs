using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth.Permissions;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>提供代码权限目录的管理员只读入口，不接受在线修改权限定义。</summary>
[ApiController]
[Authorize]
[Route("api/system/permissions")]
public sealed class RbacPermissionsController(IRbacManagementService service) : ControllerBase
{
    /// <summary>返回权限定义、分组、排序和可分配边界。</summary>
    [HttpGet]
    public Task<ServiceResult<List<ManagedPermissionDto>>> List(CancellationToken cancellationToken) =>
        service.GetPermissionsAsync(cancellationToken);
}
