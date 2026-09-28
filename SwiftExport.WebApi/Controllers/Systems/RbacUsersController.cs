using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth.Permissions;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>提供用户资料、启停、角色关系与有效权限的管理员 HTTP 边界。</summary>
[ApiController]
[Authorize]
[Route("api/system/users")]
public sealed class RbacUsersController(IRbacManagementService service) : ControllerBase
{
    /// <summary>分页查询所有未删除用户，包含停用账号。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ManagedUserDto>>> Search([FromBody] ManagedPageRequest request, CancellationToken cancellationToken) =>
        service.SearchUsersAsync(request, cancellationToken);

    /// <summary>按标识读取用户非凭据资料。</summary>
    [HttpGet("{id:guid}")]
    public Task<ServiceResult<ManagedUserDto>> Get(Guid id, CancellationToken cancellationToken) =>
        service.GetUserAsync(id, cancellationToken);

    /// <summary>按预期版本更新用户显示资料。</summary>
    [HttpPut("{id:guid}")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<ManagedUserDto>> Update(Guid id, [FromBody] UpdateManagedUserRequest request, CancellationToken cancellationToken) =>
        service.UpdateUserAsync(id, request, cancellationToken);

    /// <summary>按预期版本启停用户，保护最后一位有效管理员。</summary>
    [HttpPut("{id:guid}/status")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<ManagedUserDto>> SetStatus(Guid id, [FromBody] SetManagedStatusRequest request, CancellationToken cancellationToken) =>
        service.SetUserStatusAsync(id, request, cancellationToken);

    /// <summary>读取用户当前有效角色及其聚合版本。</summary>
    [HttpGet("{id:guid}/roles")]
    public Task<ServiceResult<UserRolesDto>> GetRoles(Guid id, CancellationToken cancellationToken) =>
        service.GetUserRolesAsync(id, cancellationToken);

    /// <summary>在事务内整组替换用户角色，并重验操作者权限。</summary>
    [HttpPut("{id:guid}/roles")]
    [PreventDuplicateRequest]
    public Task<ServiceResult<UserRolesDto>> ReplaceRoles(Guid id, [FromBody] ReplaceUserRolesRequest request, CancellationToken cancellationToken) =>
        service.ReplaceUserRolesAsync(id, request, cancellationToken);

    /// <summary>只读查看用户当前有效权限，不接受派生权限保存。</summary>
    [HttpGet("{id:guid}/effective-permissions")]
    public Task<ServiceResult<List<string>>> GetEffectivePermissions(Guid id, CancellationToken cancellationToken) =>
        service.GetEffectivePermissionsAsync(id, cancellationToken);
}
